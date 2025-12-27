# Deployment Guide: self-hosted on Hetzner

This guide covers deploying the Connected app on a single linux VPS (e.g., Hetzner, DigitalOcean) using Docker Compose. This includes the Go backend, React frontend (via Nginx), and a self-hosted Coturn STUN/TURN server.

## Prerequisites

1.  **Linux VPS**: Ubuntu 20.04+ recommended.
2.  **Domain Name**: Pointed to your VPS IP (e.g., `connected.dowhile.fun`).
3.  **Docker & Docker Compose**: Installed on the VPS.

## Configuration

### 1. Environment Variables
Create a `.env` file in the project root (same level as `docker-compose.yml`) or set these variables in your shell/CI.

```bash
# Domain name for the application
TURN_HOST=connected.dowhile.fun

# Shared secret for TURN authentication (Generate a long random string)
TURN_SECRET=super_secret_string_change_me
```

### 2. Coturn Configuration
Edit `coturn/turnserver.conf`.
**Important**: Ensure `static-auth-secret` matches `TURN_SECRET`.

```ini
# coturn/turnserver.conf
realm=connected.dowhile.fun
static-auth-secret=super_secret_string_change_me
```

### 3. Firewall
Ensure the following ports are open on your VPS firewall (e.g., UFW or Hetzner Cloud Firewall):
-   **80/tcp** (HTTP)
-   **443/tcp** (HTTPS)
-   **3478/udp & tcp** (STUN/TURN Signaling)
-   **49152-65535/udp** (WebRTC Media Range)

## Deployment

1.  Clone the repository to your VPS.
2.  Build and run the stack:
    ```bash
    docker-compose up -d --build
    ```

## HTTPS (SSL) Setup

This setup assumes `nginx` is running on port 80. For production, you **must** use HTTPS for WebRTC to work (camera/mic permissions require secure context).

### Quick Option: Certbot on Host
If you don't use an automated Nginx sidecar (like `nginx-proxy` or `traefik`), you can run Certbot on the host to generate certs and mount them into the Nginx container.

1.  Install Certbot: `sudo apt install certbot`
2.  Generate Certs: `sudo certbot certonly --standalone -d connected.dowhile.fun`
3.  Update `docker-compose.yml` to mount the certs:
    ```yaml
    nginx:
      volumes:
        - /etc/letsencrypt:/etc/letsencrypt
        # ... other volumes
    ```
4.  Update `nginx/nginx.conf` to listen on 443 and include ssl_certificate paths.

## Verification
1.  Navigate to `https://connected.dowhile.fun`.
2.  Open the browser console.
3.  Create a room.
4.  Check logs: You should see `[WebRTC] Loaded ICE Servers` with your custom TURN server URI.
