import socket


def main():
    with socket.create_connection(("localhost", 5000)) as sock:
        sock.sendall(b"testing")
        response = sock.recv(1024)
        print(response.decode("utf-8", errors="replace"))


if __name__ == "__main__":
    main()
