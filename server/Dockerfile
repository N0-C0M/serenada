# Build stage
FROM golang:1.23-alpine AS builder
WORKDIR /app
COPY go.mod go.sum ./
# If you don't have go.sum yet, this might fail, but checking previously I saw go.sum exists.
# If not, 'go mod download' might need just go.mod or running 'go mod tidy' locally first.
RUN go mod download
COPY . .
RUN go build -o server .

# Run stage
FROM alpine:latest
WORKDIR /root/
COPY --from=builder /app/server .
EXPOSE 8080
CMD ["./server"]
