package app.serenada.android.call

import android.content.Intent
import android.content.Context
import android.hardware.camera2.CameraManager
import android.hardware.display.DisplayManager
import android.media.projection.MediaProjection
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.util.DisplayMetrics
import android.util.Log
import java.util.Collections
import java.util.WeakHashMap
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt
import org.webrtc.AudioSource
import org.webrtc.AudioTrack
import org.webrtc.Camera2Enumerator
import org.webrtc.DefaultVideoDecoderFactory
import org.webrtc.DefaultVideoEncoderFactory
import org.webrtc.EglBase
import org.webrtc.IceCandidate
import org.webrtc.MediaConstraints
import org.webrtc.MediaStreamTrack
import org.webrtc.PeerConnection
import org.webrtc.PeerConnectionFactory
import org.webrtc.RendererCommon
import org.webrtc.RtpParameters
import org.webrtc.RtpTransceiver
import org.webrtc.ScreenCapturerAndroid
import org.webrtc.SessionDescription
import org.webrtc.SurfaceTextureHelper
import org.webrtc.SurfaceViewRenderer
import org.webrtc.VideoCapturer
import org.webrtc.VideoSink
import org.webrtc.VideoSource
import org.webrtc.VideoTrack

class WebRtcEngine(
    context: Context,
    private val onLocalIceCandidate: (IceCandidate) -> Unit,
    private val onConnectionState: (PeerConnection.PeerConnectionState) -> Unit,
    private val onIceConnectionState: (PeerConnection.IceConnectionState) -> Unit,
    private val onSignalingState: (PeerConnection.SignalingState) -> Unit,
    private val onRenegotiationNeededCallback: () -> Unit,
    private val onRemoteVideoTrack: (VideoTrack?) -> Unit,
    private val onCameraFacingChanged: (Boolean) -> Unit,
    private val onScreenShareStopped: () -> Unit,
    private var isRemoteBlackFrameAnalysisEnabled: Boolean = true
) {
    private enum class LocalCameraSource {
        SELFIE,
        WORLD,
        COMPOSITE
    }

    private data class CapturerSelection(
        val capturer: VideoCapturer,
        val isFrontFacing: Boolean,
        val captureProfile: CaptureProfile
    )

    private data class CaptureProfile(
        val width: Int,
        val height: Int,
        val fps: Int
    )

    private data class CapturePolicy(
        val targetWidth: Int,
        val targetHeight: Int,
        val targetFps: Int,
        val minFps: Int
    )

    private data class VideoSenderPolicy(
        val maxBitrateBps: Int,
        val minBitrateBps: Int,
        val maxFramerate: Int,
        val degradationPreference: RtpParameters.DegradationPreference
    )

    private val appContext = context.applicationContext
    private val eglBase: EglBase = EglBase.create()
    private val peerConnectionFactory: PeerConnectionFactory

    private var peerConnection: PeerConnection? = null
    private var localVideoTrack: VideoTrack? = null
    private var localAudioTrack: AudioTrack? = null
    private var videoCapturer: VideoCapturer? = null
    private var videoSource: VideoSource? = null
    private var audioSource: AudioSource? = null
    private var surfaceTextureHelper: SurfaceTextureHelper? = null
    private var currentCameraSource = LocalCameraSource.SELFIE
    private var cameraSourceBeforeScreenShare: LocalCameraSource? = null
    private var isScreenSharing = false
    private var compositeSupportCache: Pair<Pair<String, String>, Boolean>? = null
    private var compositeDisabledAfterFailure = false
    private val mainHandler = Handler(Looper.getMainLooper())

    private var localSink: VideoSink? = null
    private var remoteSink: VideoSink? = null
    private var remoteVideoTrack: VideoTrack? = null
    private val remoteBlackFrameAnalyzer = RemoteBlackFrameAnalyzer()
    private val remoteVideoStateSink = VideoSink { frame -> onRemoteVideoFrame(frame) }

    private var iceServers: List<PeerConnection.IceServer>? = null
    private var remoteDescriptionSet = false
    private val pendingIceCandidates = mutableListOf<IceCandidate>()
    private var iceCandidateCount = 0
    private val initializedRenderers =
        Collections.newSetFromMap(WeakHashMap<SurfaceViewRenderer, Boolean>())

    init {
        val initOptions = PeerConnectionFactory.InitializationOptions.builder(appContext)
            .setEnableInternalTracer(false)
            .createInitializationOptions()
        PeerConnectionFactory.initialize(initOptions)

        val encoderFactory = DefaultVideoEncoderFactory(eglBase.eglBaseContext, true, true)
        val decoderFactory = DefaultVideoDecoderFactory(eglBase.eglBaseContext)
        peerConnectionFactory = PeerConnectionFactory.builder()
            .setVideoEncoderFactory(encoderFactory)
            .setVideoDecoderFactory(decoderFactory)
            .createPeerConnectionFactory()
    }

    fun getEglContext(): EglBase.Context = eglBase.eglBaseContext

    fun startLocalMedia() {
        if (localAudioTrack != null || localVideoTrack != null) return
        val audioConstraints = MediaConstraints()
        audioSource = peerConnectionFactory.createAudioSource(audioConstraints)
        localAudioTrack = peerConnectionFactory.createAudioTrack("ARDAMSa0", audioSource)
        applyAudioTrackHints()

        videoSource = peerConnectionFactory.createVideoSource(false)
        currentCameraSource = LocalCameraSource.SELFIE
        if (!restartVideoCapturer(currentCameraSource)) {
            Log.w("WebRtcEngine", "No camera capturer available for $currentCameraSource")
            videoSource?.dispose()
            videoSource = null
            localAudioTrack?.setEnabled(false)
            localAudioTrack = null
            audioSource?.dispose()
            audioSource = null
            return
        }
        localVideoTrack = peerConnectionFactory.createVideoTrack("ARDAMSv0", videoSource)
        localVideoTrack?.setEnabled(true)
        localSink?.let { sink ->
            localVideoTrack?.addSink(sink)
        }
        createPeerConnectionIfReady()
    }

    fun stopLocalMedia() {
        isScreenSharing = false
        cameraSourceBeforeScreenShare = null
        localVideoTrack?.setEnabled(false)
        localAudioTrack?.setEnabled(false)
        disposeVideoCapturer()
        videoSource?.dispose()
        videoSource = null
        audioSource?.dispose()
        audioSource = null
        localVideoTrack = null
        localAudioTrack = null
    }

    fun closePeerConnection() {
        peerConnection?.close()
        peerConnection?.dispose()
        peerConnection = null
        remoteVideoTrack?.removeSink(remoteVideoStateSink)
        remoteVideoTrack = null
        remoteBlackFrameAnalyzer.onTrackDetached()
        remoteDescriptionSet = false
        pendingIceCandidates.clear()
        iceCandidateCount = 0
        onRemoteVideoTrack(null)
    }

    fun release() {
        stopLocalMedia()
        closePeerConnection()
    }

    fun setIceServers(servers: List<PeerConnection.IceServer>) {
        Log.d("WebRtcEngine", "ICE servers set: ${servers.size}")
        iceServers = servers
        createPeerConnectionIfReady()
    }

    fun isReady(): Boolean = peerConnection != null
    fun ensurePeerConnection() {
        createPeerConnectionIfReady()
    }
    fun getSignalingState(): PeerConnection.SignalingState? = peerConnection?.signalingState()
    fun hasRemoteDescription(): Boolean = peerConnection?.remoteDescription != null

    fun rollbackLocalDescription(onComplete: ((Boolean) -> Unit)? = null) {
        val pc = peerConnection ?: return
        val desc = SessionDescription(SessionDescription.Type.ROLLBACK, "")
        pc.setLocalDescription(object : SdpObserverAdapter() {
            override fun onSetSuccess() {
                Log.d("WebRtcEngine", "Local description rolled back")
                onComplete?.invoke(true)
            }

            override fun onSetFailure(error: String?) {
                Log.w("WebRtcEngine", "Failed to rollback local description: $error")
                onComplete?.invoke(false)
            }
        }, desc)
    }

    fun flipCamera() {
        if (isScreenSharing) return
        if (videoSource == null) return
        var target = when (currentCameraSource) {
            LocalCameraSource.SELFIE -> LocalCameraSource.WORLD
            LocalCameraSource.WORLD -> LocalCameraSource.COMPOSITE
            LocalCameraSource.COMPOSITE -> LocalCameraSource.SELFIE
        }
        if (target == LocalCameraSource.COMPOSITE && !canUseCompositeSource()) {
            Log.w("WebRtcEngine", "Composite source unavailable; skipping to ${LocalCameraSource.SELFIE}")
            target = LocalCameraSource.SELFIE
        }
        if (restartVideoCapturer(target)) {
            return
        }
        Log.w("WebRtcEngine", "Failed to switch camera source to $target")
        val fallback = if (target == LocalCameraSource.COMPOSITE) {
            compositeDisabledAfterFailure = true
            LocalCameraSource.SELFIE
        } else {
            currentCameraSource
        }
        if (fallback != target && restartVideoCapturer(fallback)) {
            Log.w("WebRtcEngine", "Camera source fallback applied: $fallback")
        }
    }

    fun createOffer(
        iceRestart: Boolean = false,
        onSdp: (String) -> Unit,
        onComplete: ((Boolean) -> Unit)? = null
    ): Boolean {
        val pc = peerConnection ?: return false
        if (pc.signalingState() != PeerConnection.SignalingState.STABLE) {
            Log.d("WebRtcEngine", "Skipping offer; signaling state is ${pc.signalingState()}")
            onComplete?.invoke(false)
            return false
        }
        Log.d("WebRtcEngine", "Creating offer")
        val constraints = MediaConstraints()
        if (iceRestart) {
            constraints.optional.add(MediaConstraints.KeyValuePair("IceRestart", "true"))
        }
        pc.createOffer(object : SdpObserverAdapter() {
            override fun onCreateSuccess(desc: SessionDescription?) {
                if (desc == null) {
                    onComplete?.invoke(false)
                    return
                }
                pc.setLocalDescription(object : SdpObserverAdapter() {
                    override fun onSetSuccess() {
                        Log.d("WebRtcEngine", "Local description set (offer)")
                        onSdp(desc.description)
                        onComplete?.invoke(true)
                    }

                    override fun onSetFailure(error: String?) {
                        Log.w("WebRtcEngine", "Failed to set local offer: $error")
                        onComplete?.invoke(false)
                    }
                }, desc)
            }

            override fun onCreateFailure(error: String?) {
                Log.w("WebRtcEngine", "Offer creation failed: $error")
                onComplete?.invoke(false)
            }
        }, constraints)
        return true
    }

    fun createAnswer(onSdp: (String) -> Unit, onComplete: ((Boolean) -> Unit)? = null) {
        val pc = peerConnection ?: return
        Log.d("WebRtcEngine", "Creating answer")
        val constraints = MediaConstraints()
        pc.createAnswer(object : SdpObserverAdapter() {
            override fun onCreateSuccess(desc: SessionDescription?) {
                if (desc == null) {
                    onComplete?.invoke(false)
                    return
                }
                pc.setLocalDescription(object : SdpObserverAdapter() {
                    override fun onSetSuccess() {
                        Log.d("WebRtcEngine", "Local description set (answer)")
                        onSdp(desc.description)
                        onComplete?.invoke(true)
                    }

                    override fun onSetFailure(error: String?) {
                        Log.w("WebRtcEngine", "Failed to set local answer: $error")
                        onComplete?.invoke(false)
                    }
                }, desc)
            }

            override fun onCreateFailure(error: String?) {
                Log.w("WebRtcEngine", "Answer creation failed: $error")
                onComplete?.invoke(false)
            }
        }, constraints)
    }

    fun setRemoteDescription(type: SessionDescription.Type, sdp: String, onComplete: (() -> Unit)? = null) {
        val pc = peerConnection ?: return
        val desc = SessionDescription(type, sdp)
        pc.setRemoteDescription(object : SdpObserverAdapter() {
            override fun onSetSuccess() {
                remoteDescriptionSet = true
                flushPendingIceCandidates()
                Log.d("WebRtcEngine", "Remote description set ($type)")
                onComplete?.invoke()
            }

            override fun onSetFailure(error: String?) {
                Log.w("WebRtcEngine", "Failed to set remote description ($type): $error")
            }
        }, desc)
    }

    fun addIceCandidate(candidate: IceCandidate) {
        val pc = peerConnection ?: return
        if (!remoteDescriptionSet) {
            pendingIceCandidates.add(candidate)
            return
        }
        pc.addIceCandidate(candidate)
    }

    fun toggleAudio(enabled: Boolean) {
        localAudioTrack?.setEnabled(enabled)
    }

    fun toggleVideo(enabled: Boolean) {
        localVideoTrack?.setEnabled(enabled)
    }

    fun startScreenShare(intent: Intent): Boolean {
        if (isScreenSharing) return true
        val observer = videoSource?.capturerObserver ?: return false
        val previousSource = currentCameraSource
        disposeVideoCapturer()
        val capturer = ScreenCapturerAndroid(intent, object : MediaProjection.Callback() {
            override fun onStop() {
                mainHandler.post {
                    if (isScreenSharing) {
                        stopScreenShare()
                        onScreenShareStopped()
                    }
                }
            }
        })
        val textureHelper = SurfaceTextureHelper.create("ScreenCaptureThread", eglBase.eglBaseContext)
        val captureProfile = selectScreenShareCaptureProfile()
        return try {
            capturer.initialize(textureHelper, appContext, observer)
            capturer.startCapture(captureProfile.width, captureProfile.height, captureProfile.fps)
            videoCapturer = capturer
            surfaceTextureHelper = textureHelper
            cameraSourceBeforeScreenShare = previousSource
            isScreenSharing = true
            applyVideoSenderParameters()
            Log.d(
                "WebRtcEngine",
                "Screen share capture profile: ${captureProfile.width}x${captureProfile.height}@${captureProfile.fps}fps"
            )
            onCameraFacingChanged(false)
            true
        } catch (e: Exception) {
            Log.w("WebRtcEngine", "Failed to start screen sharing", e)
            runCatching { capturer.dispose() }
            runCatching { textureHelper.dispose() }
            if (!restartVideoCapturer(previousSource)) {
                restartVideoCapturer(LocalCameraSource.SELFIE)
            }
            false
        }
    }

    fun stopScreenShare(): Boolean {
        if (!isScreenSharing) return true
        val sourceToRestore = cameraSourceBeforeScreenShare ?: currentCameraSource
        isScreenSharing = false
        cameraSourceBeforeScreenShare = null
        disposeVideoCapturer()
        if (!restartVideoCapturer(sourceToRestore) && !restartVideoCapturer(LocalCameraSource.SELFIE)) {
            Log.w("WebRtcEngine", "Failed to restore camera after screen sharing stop")
        }
        return true
    }

    fun setRemoteBlackFrameAnalysisEnabled(enabled: Boolean) {
        isRemoteBlackFrameAnalysisEnabled = enabled
    }

    fun isRemoteVideoTrackEnabled(): Boolean {
        val track = remoteVideoTrack ?: return false
        if (!track.enabled()) return false
        if (remoteBlackFrameAnalyzer.isVideoConsideredOff()) {
            return false
        }
        return true
    }

    fun remoteVideoDiagnostics(): String {
        val track = remoteVideoTrack
        return remoteBlackFrameAnalyzer.diagnostics(
            trackPresent = track != null,
            trackEnabled = track?.enabled() == true
        )
    }

    fun attachLocalRenderer(
        renderer: SurfaceViewRenderer,
        rendererEvents: RendererCommon.RendererEvents? = null
    ) {
        initRenderer(renderer, rendererEvents)
        attachLocalSink(renderer)
    }

    fun attachRemoteRenderer(
        renderer: SurfaceViewRenderer,
        rendererEvents: RendererCommon.RendererEvents? = null
    ) {
        initRenderer(renderer, rendererEvents)
        attachRemoteSink(renderer)
    }

    fun detachLocalRenderer(renderer: SurfaceViewRenderer) {
        detachLocalSink(renderer)
    }

    fun detachRemoteRenderer(renderer: SurfaceViewRenderer) {
        detachRemoteSink(renderer)
    }

    fun attachLocalSink(sink: VideoSink) {
        if (localSink === sink) return
        localSink?.let { previous -> localVideoTrack?.removeSink(previous) }
        localSink = sink
        localVideoTrack?.addSink(sink)
    }

    fun detachLocalSink(sink: VideoSink) {
        localVideoTrack?.removeSink(sink)
        if (localSink === sink) {
            localSink = null
        }
    }

    fun attachRemoteSink(sink: VideoSink) {
        if (remoteSink === sink) return
        remoteSink?.let { previous -> remoteVideoTrack?.removeSink(previous) }
        remoteSink = sink
        remoteVideoTrack?.addSink(sink)
    }

    fun detachRemoteSink(sink: VideoSink) {
        remoteVideoTrack?.removeSink(sink)
        if (remoteSink === sink) {
            remoteSink = null
        }
    }

    private fun initRenderer(
        renderer: SurfaceViewRenderer,
        rendererEvents: RendererCommon.RendererEvents?
    ) {
        if (!initializedRenderers.add(renderer)) {
            return
        }
        renderer.init(eglBase.eglBaseContext, rendererEvents)
        renderer.setEnableHardwareScaler(true)
    }

    private fun createPeerConnectionIfReady() {
        if (peerConnection != null) return
        val servers = iceServers ?: return
        if (localAudioTrack == null && localVideoTrack == null) return

        remoteDescriptionSet = false
        pendingIceCandidates.clear()
        val config = PeerConnection.RTCConfiguration(servers).apply {
            sdpSemantics = PeerConnection.SdpSemantics.UNIFIED_PLAN
        }
        peerConnection = peerConnectionFactory.createPeerConnection(config, object : PeerConnection.Observer {
            override fun onIceCandidate(candidate: IceCandidate) {
                iceCandidateCount += 1
                if (iceCandidateCount <= 5 || iceCandidateCount % 25 == 0) {
                    Log.d("WebRtcEngine", "Local ICE candidate #$iceCandidateCount")
                }
                onLocalIceCandidate(candidate)
            }

            override fun onConnectionChange(newState: PeerConnection.PeerConnectionState) {
                Log.d("WebRtcEngine", "Connection state: $newState")
                onConnectionState(newState)
            }

            override fun onTrack(transceiver: RtpTransceiver?) {
                val track = transceiver?.receiver?.track()
                if (track is VideoTrack) {
                    remoteVideoTrack?.removeSink(remoteVideoStateSink)
                    remoteVideoTrack = track
                    remoteBlackFrameAnalyzer.onTrackAttached()
                    Log.d(
                        "WebRtcEngine",
                        "[RemoteVideo] onTrack attached, trackEnabled=${track.enabled()}"
                    )
                    track.addSink(remoteVideoStateSink)
                    remoteSink?.let { sink -> track.addSink(sink) }
                    onRemoteVideoTrack(track)
                }
            }

            override fun onSignalingChange(newState: PeerConnection.SignalingState) {
                Log.d("WebRtcEngine", "Signaling state: $newState")
                onSignalingState(newState)
            }

            override fun onIceConnectionChange(newState: PeerConnection.IceConnectionState) {
                Log.d("WebRtcEngine", "ICE state: $newState")
                onIceConnectionState(newState)
            }

            override fun onIceConnectionReceivingChange(receiving: Boolean) {
            }

            override fun onIceGatheringChange(newState: PeerConnection.IceGatheringState) {
                Log.d("WebRtcEngine", "ICE gathering: $newState")
            }

            override fun onIceCandidatesRemoved(candidates: Array<IceCandidate>) {
            }

            override fun onAddStream(stream: org.webrtc.MediaStream) {
            }

            override fun onRemoveStream(stream: org.webrtc.MediaStream) {
            }

            override fun onDataChannel(dc: org.webrtc.DataChannel) {
            }

            override fun onRenegotiationNeeded() {
                onRenegotiationNeededCallback()
            }
        })

        peerConnection?.let { pc ->
            localAudioTrack?.let { pc.addTrack(it, listOf("serenada")) }
            localVideoTrack?.let { pc.addTrack(it, listOf("serenada")) }
            ensureReceiveTransceivers(pc)
            applyAudioSenderParameters(pc)
            applyVideoSenderParameters(pc)
        }
    }

    private fun ensureReceiveTransceivers(pc: PeerConnection) {
        if (localAudioTrack == null) {
            pc.addTransceiver(
                MediaStreamTrack.MediaType.MEDIA_TYPE_AUDIO,
                RtpTransceiver.RtpTransceiverInit(RtpTransceiver.RtpTransceiverDirection.RECV_ONLY)
            )
        }
        if (localVideoTrack == null) {
            pc.addTransceiver(
                MediaStreamTrack.MediaType.MEDIA_TYPE_VIDEO,
                RtpTransceiver.RtpTransceiverInit(RtpTransceiver.RtpTransceiverDirection.RECV_ONLY)
            )
        }
    }

    private fun flushPendingIceCandidates() {
        val pc = peerConnection ?: return
        if (pendingIceCandidates.isEmpty()) return
        val pending = pendingIceCandidates.toList()
        pendingIceCandidates.clear()
        pending.forEach { pc.addIceCandidate(it) }
    }

    private fun applyAudioTrackHints() {
        val track = localAudioTrack ?: return
        runCatching {
            val method = track.javaClass.getMethod("setContentHint", String::class.java)
            method.invoke(track, "speech")
        }.onFailure {
            Log.d("WebRtcEngine", "Audio content hint not supported")
        }
    }

    private fun applyAudioSenderParameters(pc: PeerConnection) {
        val sender = pc.senders.firstOrNull { it.track()?.kind() == "audio" } ?: return
        try {
            val params = sender.parameters
            val encodings = params.encodings
            if (encodings.isNullOrEmpty()) return
            encodings[0].maxBitrateBps = 32_000
            sender.setParameters(params)
        } catch (e: Exception) {
            Log.w("WebRtcEngine", "Failed to apply audio sender parameters", e)
        }
    }

    private fun applyVideoSenderParameters(pc: PeerConnection? = peerConnection) {
        val activePc = pc ?: return
        val sender = activePc.senders.firstOrNull { it.track()?.kind() == "video" } ?: return
        val policy = activeVideoSenderPolicy()
        try {
            val params = sender.parameters
            val encodings = params.encodings
            if (encodings.isNullOrEmpty()) return
            params.degradationPreference = policy.degradationPreference
            encodings[0].maxBitrateBps = policy.maxBitrateBps
            encodings[0].minBitrateBps = policy.minBitrateBps
            encodings[0].maxFramerate = policy.maxFramerate
            sender.setParameters(params)
            Log.d(
                "WebRtcEngine",
                "Video sender policy applied: max=${policy.maxBitrateBps} min=${policy.minBitrateBps} fps=${policy.maxFramerate} mode=${activeVideoModeLabel()}"
            )
        } catch (e: Exception) {
            Log.w("WebRtcEngine", "Failed to apply video sender parameters", e)
        }
    }

    private fun restartVideoCapturer(source: LocalCameraSource): Boolean {
        val observer = videoSource?.capturerObserver ?: return false
        disposeVideoCapturer()
        val selection = createVideoCapturer(source) ?: return false
        val textureHelper = SurfaceTextureHelper.create("CaptureThread", eglBase.eglBaseContext)
        try {
            selection.capturer.initialize(textureHelper, appContext, observer)
            selection.capturer.startCapture(
                selection.captureProfile.width,
                selection.captureProfile.height,
                selection.captureProfile.fps
            )
        } catch (e: Exception) {
            Log.w("WebRtcEngine", "Failed to start capture for $source", e)
            runCatching { selection.capturer.dispose() }
            runCatching { textureHelper.dispose() }
            return false
        }
        videoCapturer = selection.capturer
        surfaceTextureHelper = textureHelper
        currentCameraSource = source
        onCameraFacingChanged(selection.isFrontFacing)
        applyVideoSenderParameters()
        Log.d(
            "WebRtcEngine",
            "Camera source active: $source (${selection.captureProfile.width}x${selection.captureProfile.height}@${selection.captureProfile.fps}fps)"
        )
        return true
    }

    private fun disposeVideoCapturer() {
        try {
            videoCapturer?.stopCapture()
        } catch (e: Exception) {
            Log.w("WebRtcEngine", "Failed to stop capture", e)
        }
        runCatching { videoCapturer?.dispose() }
        videoCapturer = null
        runCatching { surfaceTextureHelper?.dispose() }
        surfaceTextureHelper = null
    }

    private fun createVideoCapturer(source: LocalCameraSource): CapturerSelection? {
        val enumerator = Camera2Enumerator(appContext)
        val front = enumerator.deviceNames.firstOrNull { enumerator.isFrontFacing(it) }
        val back = enumerator.deviceNames.firstOrNull { enumerator.isBackFacing(it) }
        return when (source) {
            LocalCameraSource.SELFIE -> {
                if (front != null) {
                    enumerator.createCapturer(front, null)?.let {
                        CapturerSelection(
                            capturer = it,
                            isFrontFacing = true,
                            captureProfile = selectCameraCaptureProfile(
                                enumerator = enumerator,
                                deviceNames = listOf(front),
                                policy = cameraCapturePolicyFor(LocalCameraSource.SELFIE)
                            )
                        )
                    }
                } else if (back != null) {
                    enumerator.createCapturer(back, null)?.let {
                        CapturerSelection(
                            capturer = it,
                            isFrontFacing = false,
                            captureProfile = selectCameraCaptureProfile(
                                enumerator = enumerator,
                                deviceNames = listOf(back),
                                policy = cameraCapturePolicyFor(LocalCameraSource.SELFIE)
                            )
                        )
                    }
                } else {
                    null
                }
            }

            LocalCameraSource.WORLD -> {
                if (back == null) {
                    null
                } else {
                    enumerator.createCapturer(back, null)?.let {
                        CapturerSelection(
                            capturer = it,
                            isFrontFacing = false,
                            captureProfile = selectCameraCaptureProfile(
                                enumerator = enumerator,
                                deviceNames = listOf(back),
                                policy = cameraCapturePolicyFor(LocalCameraSource.WORLD)
                            )
                        )
                    }
                }
            }

            LocalCameraSource.COMPOSITE -> {
                if (front == null || back == null) {
                    null
                } else if (!canUseCompositeSource(frontDevice = front, backDevice = back)) {
                    null
                } else {
                    val mainCapturer = enumerator.createCapturer(back, null)
                    val overlayCapturer = enumerator.createCapturer(front, null)
                    if (mainCapturer == null || overlayCapturer == null) {
                        mainCapturer?.dispose()
                        overlayCapturer?.dispose()
                        null
                    } else {
                        val profile = selectCameraCaptureProfile(
                            enumerator = enumerator,
                            deviceNames = listOf(back, front),
                            policy = cameraCapturePolicyFor(LocalCameraSource.COMPOSITE)
                        )
                        CapturerSelection(
                            capturer = CompositeCameraCapturer(
                                context = appContext,
                                eglContext = eglBase.eglBaseContext,
                                mainCapturer = mainCapturer,
                                overlayCapturer = overlayCapturer,
                                onStartFailure = { onCompositeStartFailure() }
                            ),
                            isFrontFacing = false,
                            captureProfile = profile
                        )
                    }
                }
            }
        }
    }

    private fun cameraCapturePolicyFor(source: LocalCameraSource): CapturePolicy {
        return when (source) {
            LocalCameraSource.COMPOSITE -> {
                CapturePolicy(
                    targetWidth = COMPOSITE_TARGET_WIDTH,
                    targetHeight = COMPOSITE_TARGET_HEIGHT,
                    targetFps = COMPOSITE_TARGET_FPS,
                    minFps = COMPOSITE_MIN_FPS
                )
            }

            else -> {
                CapturePolicy(
                    targetWidth = CAMERA_TARGET_WIDTH,
                    targetHeight = CAMERA_TARGET_HEIGHT,
                    targetFps = CAMERA_TARGET_FPS,
                    minFps = CAMERA_MIN_FPS
                )
            }
        }
    }

    private fun activeVideoModeLabel(): String {
        if (isScreenSharing) return "screen_share"
        return when (currentCameraSource) {
            LocalCameraSource.SELFIE -> "selfie"
            LocalCameraSource.WORLD -> "world"
            LocalCameraSource.COMPOSITE -> "composite"
        }
    }

    private fun activeVideoSenderPolicy(): VideoSenderPolicy {
        if (isScreenSharing) {
            return VideoSenderPolicy(
                maxBitrateBps = SCREEN_SHARE_MAX_BITRATE_BPS,
                minBitrateBps = SCREEN_SHARE_MIN_BITRATE_BPS,
                maxFramerate = SCREEN_SHARE_TARGET_FPS,
                degradationPreference = RtpParameters.DegradationPreference.MAINTAIN_RESOLUTION
            )
        }
        return when (currentCameraSource) {
            LocalCameraSource.COMPOSITE -> VideoSenderPolicy(
                maxBitrateBps = COMPOSITE_MAX_BITRATE_BPS,
                minBitrateBps = COMPOSITE_MIN_BITRATE_BPS,
                maxFramerate = COMPOSITE_TARGET_FPS,
                degradationPreference = RtpParameters.DegradationPreference.MAINTAIN_FRAMERATE
            )

            else -> VideoSenderPolicy(
                maxBitrateBps = CAMERA_MAX_BITRATE_BPS,
                minBitrateBps = CAMERA_MIN_BITRATE_BPS,
                maxFramerate = CAMERA_TARGET_FPS,
                degradationPreference = RtpParameters.DegradationPreference.BALANCED
            )
        }
    }

    private fun selectCameraCaptureProfile(
        enumerator: Camera2Enumerator,
        deviceNames: List<String>,
        policy: CapturePolicy
    ): CaptureProfile {
        if (deviceNames.isEmpty()) {
            return defaultCaptureProfile(policy)
        }
        val formatsByDevice = deviceNames.mapNotNull { deviceName ->
            val bestFpsByResolution = (enumerator.getSupportedFormats(deviceName) ?: emptyList())
                .groupBy { Pair(it.width, it.height) }
                .mapValues { (_, formats) ->
                    formats.maxOfOrNull { format -> normalizeFps(format.framerate.max) } ?: policy.targetFps
                }
            if (bestFpsByResolution.isEmpty()) null else bestFpsByResolution
        }
        if (formatsByDevice.isEmpty()) {
            return defaultCaptureProfile(policy)
        }

        var commonResolutions: Set<Pair<Int, Int>> = formatsByDevice.first().keys
        formatsByDevice.drop(1).forEach { map ->
            commonResolutions = commonResolutions.intersect(map.keys)
        }
        if (commonResolutions.isNotEmpty()) {
            val commonFpsByResolution = commonResolutions.associateWith { size ->
                formatsByDevice.minOf { formatMap ->
                    formatMap[size] ?: policy.targetFps
                }
            }
            chooseProfileForPolicy(commonFpsByResolution, policy)?.let { return it }
        }

        val perDeviceProfiles = formatsByDevice.mapNotNull { map ->
            chooseProfileForPolicy(map, policy)
        }
        if (perDeviceProfiles.isNotEmpty()) {
            return CaptureProfile(
                width = perDeviceProfiles.minOf { it.width },
                height = perDeviceProfiles.minOf { it.height },
                fps = perDeviceProfiles.minOf { it.fps }
            )
        }

        return defaultCaptureProfile(policy)
    }

    private fun chooseProfileForPolicy(
        fpsByResolution: Map<Pair<Int, Int>, Int>,
        policy: CapturePolicy
    ): CaptureProfile? {
        val profiles = fpsByResolution.map { (size, fps) ->
            CaptureProfile(
                width = normalizeDimension(size.first),
                height = normalizeDimension(size.second),
                fps = normalizeFps(fps)
            )
        }
        if (profiles.isEmpty()) return null
        val inTargetBounds = profiles.filter { profileFitsPolicyBounds(it, policy) }
        val candidatePool = if (inTargetBounds.isNotEmpty()) inTargetBounds else profiles
        val targetArea = policy.targetWidth * policy.targetHeight
        val targetFps = policy.targetFps
        val minFps = policy.minFps
        val chosen = candidatePool.minWithOrNull(
            compareBy<CaptureProfile>(
                { if (it.fps >= minFps) 0 else 1 },
                { if (it.width * it.height <= targetArea) 0 else 1 },
                { abs((it.width * it.height) - targetArea) },
                { abs(it.fps - targetFps) },
                { -(it.width * it.height) },
                { -it.fps }
            )
        ) ?: return null
        val selectedFps = if (chosen.fps >= minFps) {
            min(chosen.fps, targetFps)
        } else {
            chosen.fps
        }
        return chosen.copy(fps = normalizeFps(selectedFps))
    }

    private fun profileFitsPolicyBounds(profile: CaptureProfile, policy: CapturePolicy): Boolean {
        val profileLong = max(profile.width, profile.height)
        val profileShort = min(profile.width, profile.height)
        val targetLong = max(policy.targetWidth, policy.targetHeight)
        val targetShort = min(policy.targetWidth, policy.targetHeight)
        return profileLong <= targetLong && profileShort <= targetShort
    }

    private fun selectScreenShareCaptureProfile(): CaptureProfile {
        val (rawWidth, rawHeight) = readDisplaySize()
        val (width, height) = clampResolutionToTarget(
            width = rawWidth,
            height = rawHeight,
            targetWidth = SCREEN_SHARE_MAX_WIDTH,
            targetHeight = SCREEN_SHARE_MAX_HEIGHT
        )
        val displayFps = readDisplayFps()
        val fps = displayFps.coerceIn(SCREEN_SHARE_MIN_FPS, SCREEN_SHARE_TARGET_FPS)
        return CaptureProfile(
            width = width,
            height = height,
            fps = normalizeFps(fps)
        )
    }

    private fun readDisplaySize(): Pair<Int, Int> {
        val windowManager = appContext.getSystemService(Context.WINDOW_SERVICE) as? android.view.WindowManager
        if (windowManager != null) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                val bounds = windowManager.currentWindowMetrics.bounds
                if (bounds.width() > 0 && bounds.height() > 0) {
                    return Pair(bounds.width(), bounds.height())
                }
            } else {
                val metrics = DisplayMetrics()
                @Suppress("DEPRECATION")
                windowManager.defaultDisplay?.getRealMetrics(metrics)
                if (metrics.widthPixels > 0 && metrics.heightPixels > 0) {
                    return Pair(metrics.widthPixels, metrics.heightPixels)
                }
            }
        }
        return Pair(SCREEN_SHARE_MAX_WIDTH, SCREEN_SHARE_MAX_HEIGHT)
    }

    private fun clampResolutionToTarget(
        width: Int,
        height: Int,
        targetWidth: Int,
        targetHeight: Int
    ): Pair<Int, Int> {
        val safeWidth = width.coerceAtLeast(2)
        val safeHeight = height.coerceAtLeast(2)
        val scale = min(
            1.0,
            min(
                targetWidth.toDouble() / safeWidth.toDouble(),
                targetHeight.toDouble() / safeHeight.toDouble()
            )
        )
        val scaledWidth = normalizeDimension((safeWidth * scale).roundToInt())
        val scaledHeight = normalizeDimension((safeHeight * scale).roundToInt())
        return Pair(scaledWidth.coerceAtLeast(2), scaledHeight.coerceAtLeast(2))
    }

    private fun readDisplayFps(): Int {
        val displayManager = appContext.getSystemService(Context.DISPLAY_SERVICE) as? DisplayManager
        @Suppress("DEPRECATION")
        val refreshRate = displayManager?.getDisplay(android.view.Display.DEFAULT_DISPLAY)?.refreshRate
        if (refreshRate != null && refreshRate > 0f) {
            return refreshRate.roundToInt()
        }
        return SCREEN_SHARE_TARGET_FPS
    }

    private fun normalizeDimension(value: Int): Int {
        val positive = value.coerceAtLeast(2)
        return if (positive % 2 == 0) positive else positive - 1
    }

    private fun normalizeFps(value: Int): Int {
        val normalized = if (value > 1000) value / 1000 else value
        return normalized.coerceIn(1, MAX_CAPTURE_FPS)
    }

    private fun defaultCaptureProfile(policy: CapturePolicy): CaptureProfile {
        return CaptureProfile(
            width = normalizeDimension(policy.targetWidth),
            height = normalizeDimension(policy.targetHeight),
            fps = normalizeFps(policy.targetFps)
        )
    }

    private fun onCompositeStartFailure() {
        mainHandler.post {
            if (currentCameraSource != LocalCameraSource.COMPOSITE) return@post
            if (videoCapturer !is CompositeCameraCapturer) return@post
            compositeDisabledAfterFailure = true
            Log.w("WebRtcEngine", "Composite source failed; disabling composite and falling back to selfie")
            if (restartVideoCapturer(LocalCameraSource.SELFIE)) {
                Log.w("WebRtcEngine", "Camera source fallback applied: ${LocalCameraSource.SELFIE}")
            }
        }
    }

    private fun canUseCompositeSource(): Boolean {
        val enumerator = Camera2Enumerator(appContext)
        val front = enumerator.deviceNames.firstOrNull { enumerator.isFrontFacing(it) } ?: return false
        val back = enumerator.deviceNames.firstOrNull { enumerator.isBackFacing(it) } ?: return false
        return canUseCompositeSource(frontDevice = front, backDevice = back)
    }

    private fun canUseCompositeSource(frontDevice: String, backDevice: String): Boolean {
        if (compositeDisabledAfterFailure) {
            return false
        }
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            // API < 30 has no stable concurrent-camera query API, so allow trial.
            return true
        }
        val cacheKey = Pair(frontDevice, backDevice)
        compositeSupportCache?.let { (savedKey, savedValue) ->
            if (savedKey == cacheKey) {
                return savedValue
            }
        }
        val manager = appContext.getSystemService(CameraManager::class.java) ?: return false
        val supported = runCatching {
            manager.concurrentCameraIds.any { ids ->
                ids.contains(frontDevice) && ids.contains(backDevice)
            }
        }.getOrDefault(false)
        compositeSupportCache = Pair(cacheKey, supported)
        if (!supported) {
            Log.w(
                "WebRtcEngine",
                "Composite source unsupported by concurrent camera constraints. front=$frontDevice back=$backDevice"
            )
        }
        return supported
    }

    private fun onRemoteVideoFrame(frame: org.webrtc.VideoFrame) {
        val stateChanged =
            remoteBlackFrameAnalyzer.onFrame(
                frame = frame,
                blackFrameAnalysisEnabled = isRemoteBlackFrameAnalysisEnabled
            )
        if (stateChanged) {
            val syntheticBlackNow = remoteBlackFrameAnalyzer.isSyntheticBlackDetected()
            Log.d(
                "WebRtcEngine",
                "[RemoteVideo] syntheticBlack=$syntheticBlackNow trackEnabled=${remoteVideoTrack?.enabled()} analyzerEnabled=$isRemoteBlackFrameAnalysisEnabled"
            )
        }
    }

    private companion object {
        const val CAMERA_TARGET_WIDTH = 1280
        const val CAMERA_TARGET_HEIGHT = 720
        const val CAMERA_TARGET_FPS = 30
        const val CAMERA_MIN_FPS = 20

        const val COMPOSITE_TARGET_WIDTH = 960
        const val COMPOSITE_TARGET_HEIGHT = 540
        const val COMPOSITE_TARGET_FPS = 24
        const val COMPOSITE_MIN_FPS = 15

        const val SCREEN_SHARE_MAX_WIDTH = 1920
        const val SCREEN_SHARE_MAX_HEIGHT = 1080
        const val SCREEN_SHARE_TARGET_FPS = 30
        const val SCREEN_SHARE_MIN_FPS = 15

        const val CAMERA_MAX_BITRATE_BPS = 2_500_000
        const val CAMERA_MIN_BITRATE_BPS = 350_000
        const val COMPOSITE_MAX_BITRATE_BPS = 1_500_000
        const val COMPOSITE_MIN_BITRATE_BPS = 300_000
        const val SCREEN_SHARE_MAX_BITRATE_BPS = 5_000_000
        const val SCREEN_SHARE_MIN_BITRATE_BPS = 1_000_000

        const val MAX_CAPTURE_FPS = 60
    }
}
