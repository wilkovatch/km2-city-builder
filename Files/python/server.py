import socket, sys, psutil, threading

sys.path.append(sys.argv[3])

import mesh_formats, texture_formats

host = "127.0.0.1"
port = int(sys.argv[1])
buf_size = 1024*16

def handle_client(conn):
    while True:
        data = conn.recv(buf_size)
        if not data:
            print("Error: no data")
            break
        else:
            message = str(data.decode("utf-8"))
            print(message)
            message_parts = message.split("|")
            if len(message_parts) < 2:
                response = bytearray(4+1)
            elif message_parts[0] == "texture":
                path = message_parts[1]
                response = texture_formats.read_file(path)
            elif message_parts[0] == "mesh":
                path = message_parts[1]
                response = mesh_formats.read_file(path)
            length = bytearray((len(response)-4).to_bytes(4, 'little'))
            response[0:4] = length
            conn.sendall(response)

s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.settimeout(1)
pid = int(sys.argv[2])
p = psutil.Process(pid) if pid >=0 else None
s.bind((host, port))
print('Starting server on port ' + str(port) +  '...')
try:
    while True:
        try:
            s.listen()
            conn, addr = s.accept()
            print('New connection:', addr)
            t = threading.Thread(target=handle_client, args=(conn, ))
            t.start()
        except socket.timeout:
            if p is None or p.status() == 'running':
                pass
            else:
                s.close()
                break
except KeyboardInterrupt:
    s.close()
