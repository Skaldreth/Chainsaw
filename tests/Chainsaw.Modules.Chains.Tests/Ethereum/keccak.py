"""An independent Keccak-256 (original padding) to make the test vectors for Chainsaw's own; nothing here touches C#."""
import sys

RC = [0x1, 0x8082, 0x800000000000808A, 0x8000000080008000, 0x808B, 0x80000001, 0x8000000080008081,
      0x8000000000008009, 0x8A, 0x88, 0x80008009, 0x8000000A, 0x8000808B, 0x800000000000008B, 0x8000000000008089,
      0x8000000000008003, 0x8000000000008002, 0x8000000000000080, 0x800A, 0x800000008000000A, 0x8000000080008081,
      0x8000000000008080, 0x80000001, 0x8000000080008008]
M = (1 << 64) - 1


def rot(v, n):
    return ((v << n) | (v >> (64 - n))) & M if n else v


def f(a):
    for rc in RC:
        c = [a[x][0] ^ a[x][1] ^ a[x][2] ^ a[x][3] ^ a[x][4] for x in range(5)]
        d = [c[(x - 1) % 5] ^ rot(c[(x + 1) % 5], 1) for x in range(5)]
        a = [[a[x][y] ^ d[x] for y in range(5)] for x in range(5)]
        r = [[0] * 5 for _ in range(5)]
        x, y, cur = 1, 0, a[1][0]
        b = [[0] * 5 for _ in range(5)]
        b[0][0] = a[0][0]
        for t in range(24):
            x, y = y, (2 * x + 3 * y) % 5
            nxt = a[x][y]
            b[x][y] = rot(cur, ((t + 1) * (t + 2) // 2) % 64)
            cur = nxt
        a = [[b[x][y] ^ (~b[(x + 1) % 5][y] & b[(x + 2) % 5][y]) for y in range(5)] for x in range(5)]
        a[0][0] ^= rc
    return a


def keccak256(data):
    rate = 136
    data = bytearray(data)
    pad = rate - len(data) % rate
    data += b"\x00" * pad
    data[len(data) - pad] ^= 0x01
    data[-1] ^= 0x80
    a = [[0] * 5 for _ in range(5)]
    for off in range(0, len(data), rate):
        for i in range(rate // 8):
            a[i % 5][i // 5] ^= int.from_bytes(data[off + 8 * i:off + 8 * i + 8], "little")
        a = f(a)
    return b"".join(a[i % 5][i // 5].to_bytes(8, "little") for i in range(4)).hex()


if __name__ == "__main__":
    for text in sys.argv[1:]:
        print(text[:20], keccak256(text.encode()))
