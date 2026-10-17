import json
import sys
import os
import base64
import struct

class DecodeExportedJson:
    def __init__(self):
        pass

    def uncompress_facade(self, v):
        v2 = {}
        for k, e in v.items():
            v2[k] = []
            for i in range(len(e)):
                v2[k].append([])
                for j in range(len(e[i])):
                    dec = base64.b64decode(e[i][j])
                    v2[k][i].append(struct.unpack('<%df' % 16, dec[4:]))
        return v2

    def uncompress_data(self, k, v):
        if k is None: return (k, v)
        parts = k.split('_')
        t = parts[0]
        k2 = k
        if t[0:3] == "b64":
            k2 = k[len(t)+1:]
            if v is None: return (k2, v)
            v2 = base64.b64decode(v)
            if t == "b64v3":
                v = struct.unpack('<f f f', v2)
            elif t == "b64v2":
                v = struct.unpack('<f f', v2)
            elif t == "b64q":
                v = struct.unpack('<f f f f', v2)
            else:
                c = struct.unpack('<I', v2[:4])[0]
                if t == "b64ia":
                    v = [struct.unpack('<I', v2[(4+4*i):(4+4*(i+1))])[0] for i in range(c)]
                elif t == "b64v3a":
                    v = [struct.unpack('<f f f', v2[(4+12*i):(4+12*(i+1))]) for i in range(c//3)]
                elif t == "b64v2a":
                    v = [struct.unpack('<f f', v2[(4+8*i):(4+8*(i+1))]) for i in range(c//2)]
        return (k2, v)

    def uncompress_elem(self, d2, k, v):
        if isinstance(v, dict):
            if k is None:
                return (k, self.uncompress_dict(v))
            parts = k.split('_')
            t = parts[0]
            if t == "b64f":
                k2 = k[len(t)+1:]
                return (k2, self.uncompress_facade(v))
            else:
                return (k, self.uncompress_dict(v))
        elif isinstance(v, list):
            return (k, self.uncompress_list(v))
        else:
            return self.uncompress_data(k, v)

    def uncompress_list(self, l):
        l2 = []
        for v in l:
            l2.append(self.uncompress_elem(l2, None, v)[1])
        return l2

    def uncompress_dict(self, d):
        d2 = {}
        for k, v in d.items():
            r = self.uncompress_elem(d2, k, v)
            d2[r[0]] = r[1]
        return d2

    def decode_json(self, file):
        with open(file) as json_file:
            data = json.load(json_file)
            data = self.uncompress_dict(data)
        return data
