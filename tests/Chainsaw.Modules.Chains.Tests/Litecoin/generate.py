"""Writes the synthetic Litecoin responses for the tests, in the form litecoinspace.org's Esplora API gives them.

Nothing here is a real address or transaction: every hash is a repeated byte. The addresses are written with
Litecoin's version bytes (0x30 for L..., 0x32 for M...) and its Bech32 prefix (ltc), and the peg-in to MWEB is a
witness program of version 9 (ltc1f...), as the API writes it with scriptpubkey_type "unknown" and a Bech32 checksum.

Run from this folder: python3 generate.py
"""
import hashlib
import json

ALPHABET = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz'
CHARSET = 'qpzry9x8gf2tvdw0s3jn54khce6mua7l'
BECH32M = 0x2bc830a3


def base58check(payload):
    data = payload + hashlib.sha256(hashlib.sha256(payload).digest()).digest()[:4]
    number = int.from_bytes(data, 'big')
    text = ''
    while number:
        number, rest = divmod(number, 58)
        text = ALPHABET[rest] + text
    return '1' * (len(data) - len(data.lstrip(b'\0'))) + text


def polymod(values):
    generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3]
    check = 1
    for value in values:
        top = check >> 25
        check = (check & 0x1ffffff) << 5 ^ value
        for i in range(5):
            check ^= generator[i] if (top >> i) & 1 else 0
    return check


def convert(data):
    acc, bits, out = 0, 0, []
    for value in data:
        acc = (acc << 8) | value
        bits += 8
        while bits >= 5:
            bits -= 5
            out.append((acc >> bits) & 31)
    if bits:
        out.append((acc << (5 - bits)) & 31)
    return out


def segwit(hrp, version, program):
    data = [version] + convert(program)
    # Litecoin writes its MWEB addresses (versions 8 and 9) with Bech32, as litecoinspace.org does.
    const = 1 if version == 0 or (hrp == 'ltc' and version in (8, 9)) else BECH32M
    expanded = [ord(c) >> 5 for c in hrp] + [0] + [ord(c) & 31 for c in hrp]
    value = polymod(expanded + data + [0] * 6) ^ const
    checksum = [(value >> 5 * (5 - i)) & 31 for i in range(6)]
    return hrp + '1' + ''.join(CHARSET[d] for d in data + checksum)


def output(script, kind, address, value):
    entry = {'scriptpubkey': script, 'scriptpubkey_asm': '(asm)', 'scriptpubkey_type': kind, 'value': value}
    if address is not None:
        entry['scriptpubkey_address'] = address
    return entry


KEY = bytes([0x11] * 20)
WPKH = bytes([0x33] * 20)
SCRIPT = bytes([0x77] * 20)
PEG_IN = bytes([0x66] * 32)
HOG = bytes([0x88] * 32)

ADDRESSES = {
    'legacy': base58check(b'\x30' + KEY),
    'wpkh': segwit('ltc', 0, WPKH),
    'p2sh': base58check(b'\x32' + SCRIPT),
    'p2sh-old': base58check(b'\x05' + SCRIPT),
    'pegin': segwit('ltc', 9, PEG_IN),
    'hog': segwit('ltc', 8, HOG),
}

BLOCK = 'c0' * 32
STATUS = {'confirmed': True, 'block_height': 3000000, 'block_hash': BLOCK, 'block_time': 1793872800}

PEG_IN_TX = {
    'txid': 'd1' * 32, 'version': 2, 'locktime': 2999999,
    'vin': [{
        'txid': 'e1' * 32, 'vout': 0,
        'prevout': output('76a914' + KEY.hex() + '88ac', 'p2pkh', ADDRESSES['legacy'], 300000000),
        'scriptsig': '', 'scriptsig_asm': '', 'witness': ['30'], 'is_coinbase': False, 'sequence': 4294967293,
    }],
    'vout': [
        output('5920' + PEG_IN.hex(), 'unknown', ADDRESSES['pegin'], 150000000),
        output('0014' + WPKH.hex(), 'v0_p2wpkh', ADDRESSES['wpkh'], 99990000),
        output('a914' + SCRIPT.hex() + '87', 'p2sh', ADDRESSES['p2sh'], 50000000),
    ],
    'size': 250, 'weight': 700, 'sigops': 1, 'fee': 10000, 'status': STATUS,
}

HOG_EX = {
    'txid': 'd2' * 32, 'version': 2, 'locktime': 0,
    'vin': [
        {'txid': 'e2' * 32, 'vout': 0, 'prevout': output('5820' + HOG.hex(), 'unknown', ADDRESSES['hog'], 900000000),
         'scriptsig': '', 'scriptsig_asm': '', 'is_coinbase': False, 'sequence': 4294967295},
        {'txid': 'd1' * 32, 'vout': 0,
         'prevout': output('5920' + PEG_IN.hex(), 'unknown', ADDRESSES['pegin'], 150000000),
         'scriptsig': '', 'scriptsig_asm': '', 'is_coinbase': False, 'sequence': 4294967295},
    ],
    'vout': [
        output('5820' + bytes([0x99] * 32).hex(), 'unknown', segwit('ltc', 8, bytes([0x99] * 32)), 950000000),
        output('0014' + WPKH.hex(), 'v0_p2wpkh', ADDRESSES['wpkh'], 100000000),
    ],
    'size': 150, 'weight': 600, 'sigops': 0, 'fee': 0, 'status': STATUS,
}

BLOCK_JSON = {
    'id': BLOCK, 'height': 3000000, 'version': 536870912, 'timestamp': 1793872800, 'tx_count': 40,
    'size': 20000, 'weight': 60000, 'merkle_root': '5f' * 32, 'previousblockhash': 'bf' * 32,
    'mediantime': 1793872000, 'nonce': 1, 'bits': 422279990, 'difficulty': 1.0,
}


def write(name, value):
    with open(name, 'w', encoding='utf-8') as file:
        json.dump(value, file, indent=1, ensure_ascii=False)


write('tx-pegin.json', PEG_IN_TX)
write('tx-hogex.json', HOG_EX)
write('block.json', BLOCK_JSON)
write('addresses.json', ADDRESSES)
with open('tip-height.txt', 'w', encoding='utf-8') as tip:
    tip.write('3000010')
