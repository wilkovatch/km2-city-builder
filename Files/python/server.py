import importlib, importlib.util, socket, struct, sys, psutil, threading, traceback

sys.path.append(sys.argv[3])

def import_optional(name):
    # a module of the core's server folder, None if the core has none
    return importlib.import_module(name) if importlib.util.find_spec(name) is not None else None

mesh_formats = import_optional('mesh_formats')
texture_formats = import_optional('texture_formats')
mesh_transforms = import_optional('mesh_transforms')

host = "127.0.0.1"
port = int(sys.argv[1])
buf_size = 1024*16

def read_transform(path):
    # the transform of a mesh from its placement point (mesh_transforms.read_transform): uint8 1 if there is one, the position
    # (3 floats), the rotation (quaternion x, y, z, w) and the scale (3 floats), then a warning (uint16 length + ASCII)
    try:
        transform = mesh_transforms.read_transform(path) if mesh_transforms is not None else None
    except Exception:
        transform = {'warning': 'Error while reading the transform of ' + path + ':\n' + traceback.format_exc()}
    values = None
    warning = ''
    if isinstance(transform, dict):
        warning = transform.get('warning') or ''
        if any(k in transform for k in ('position', 'rotation', 'scale')):
            values = list(transform.get('position', (0, 0, 0))) + list(transform.get('rotation', (0, 0, 0, 1))) + list(transform.get('scale', (1, 1, 1)))
    res = bytearray(4)
    res += struct.pack('<B', 1 if values is not None else 0)
    res += struct.pack('<10f', *(values if values is not None else (0, 0, 0, 0, 0, 0, 1, 1, 1, 1)))
    warning = warning.encode('ascii', 'replace')[:65535]
    res += struct.pack('<H', len(warning)) + warning
    return res

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
            elif message_parts[0] == "texture" and texture_formats is not None:
                path = message_parts[1]
                response = texture_formats.read_file(path)
            elif message_parts[0] == "mesh" and mesh_formats is not None:
                path = message_parts[1]
                response = mesh_formats.read_file(path)
            elif message_parts[0] == "meshTransform":
                path = message_parts[1]
                response = read_transform(path)
            else:
                response = bytearray(4+1)
            length = bytearray((len(response)-4).to_bytes(4, 'little'))
            response[0:4] = length
            conn.sendall(response)

s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.settimeout(1)
pid = int(sys.argv[2])
p = psutil.Process(pid) if pid >=0 else None

def parent_alive():
    # status() is 'running' only while the process is using the CPU on some systems (e.g. 'sleeping' when idle on Linux)
    if p is None:
        return True
    try:
        return p.is_running() and p.status() != psutil.STATUS_ZOMBIE
    except psutil.NoSuchProcess:
        return False

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
            if parent_alive():
                pass
            else:
                s.close()
                break
except KeyboardInterrupt:
    s.close()
