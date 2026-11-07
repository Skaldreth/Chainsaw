"""Writes the synthetic Blockscout responses for the Ethereum tests: made-up addresses (all lower case, so they carry
no checksum) and hashes that belong to nobody, in the shapes Blockscout answers with."""
import json

from keccak import keccak256

A = "0x" + "11" * 20  # the address the histories are about
B = "0x" + "22" * 20
C = "0x" + "33" * 20
ROUTER = "0x" + "55" * 20
FOO = "0x" + "44" * 20  # a token Chainsaw does not know
USDT = "0xdac17f958d2ee523a2206206994597c13d831ec7"
H1, H2, H3, H4 = ("0x" + h * 32 for h in ("b1", "b2", "b3", "b4"))
BLOCK = "0x" + "c9" * 32
TRANSFER = "0x" + keccak256(b"Transfer(address,address,uint256)")
TIME = "1793872800"  # 2026-11-01 10:00 UTC


def word(address):
    return "0x" + "0" * 24 + address[2:]


def amount(value):
    return "0x" + format(value, "064x")


def write(name, value):
    with open(name, "w", encoding="utf-8") as file:
        json.dump(value, file, indent=1)
        file.write("\n")


def log(contract, frm, to, value, index, extra=None):
    topics = [TRANSFER, word(frm), word(to)] + (extra or [None])
    return {"address": contract, "topics": topics, "data": amount(value), "index": str(index)}


write("tip.json", {"jsonrpc": "2.0", "result": hex(1000), "id": 1})
write("tip-shallow.json", {"jsonrpc": "2.0", "result": hex(920), "id": 1})
write("block-900.json", {"hash": BLOCK, "height": 900, "timestamp": "2026-11-01T10:00:00.000000Z"})
write("block-wrong.json", {"hash": BLOCK, "height": 901, "timestamp": "2026-11-01T10:00:12.000000Z"})
write("tx-b1.json", {"status": "1", "message": "OK", "result": {
    "hash": H1, "blockNumber": "900", "timeStamp": TIME, "from": A, "to": ROUTER,
    "value": "1000000000000000000", "gasUsed": "21000", "gasPrice": "1000000000", "success": True,
    "logs": [
        log(USDT, A, B, 52_000_000_000, 7),
        log(FOO, B, C, 5, 8),
        {"address": ROUTER, "topics": ["0x" + "ab" * 32, None, None, None], "data": "0x", "index": "9"},
        log(FOO, B, C, 0, 10, [word(C)]),  # four topics: an NFT's Transfer, not an amount
        log(USDT, A, "0x" + "77" * 20, 0, 11),  # a transfer of zero to a lookalike: address poisoning
    ]}})
write("tx-b1-internal.json", {"status": "1", "message": "OK", "result": [
    {"transactionHash": H1, "blockNumber": "900", "timeStamp": TIME, "from": ROUTER, "to": C,
     "value": "500000000000000000", "isError": "0", "index": "1", "contractAddress": "", "type": "call"},
    {"transactionHash": H1, "blockNumber": "900", "timeStamp": TIME, "from": ROUTER, "to": B,
     "value": "7", "isError": "1", "index": "2", "contractAddress": "", "type": "call"},
    {"transactionHash": H1, "blockNumber": "900", "timeStamp": TIME, "from": ROUTER, "to": B,
     "value": "0", "isError": "0", "index": "3", "contractAddress": "", "type": "call"},
    {"transactionHash": H1, "blockNumber": "900", "timeStamp": TIME, "from": ROUTER, "to": B,
     "value": "9", "isError": "0", "index": "4", "contractAddress": "", "type": "call", "callType": "delegatecall"},
]})
write("token-foo.json", {"status": "1", "message": "OK", "result": {
    "contractAddress": FOO, "decimals": "18", "symbol": "FOO", "name": "Foo", "type": "ERC-20"}})
write("tx-b2-failed.json", {"status": "1", "message": "OK", "result": {
    "hash": H2, "blockNumber": "900", "timeStamp": TIME, "from": A, "to": B, "value": "5", "gasUsed": "30000",
    "gasPrice": "2000000000", "success": False, "logs": []}})
write("tx-b2-internal.json", {"status": "0", "message": "No internal transactions found", "result": []})
write("tx-other.json", {"status": "1", "message": "OK", "result": {
    "hash": H3, "blockNumber": "900", "timeStamp": TIME, "from": A, "to": B, "value": "5", "gasUsed": "1",
    "gasPrice": "1", "success": True, "logs": []}})
H5 = "0x" + "b5" * 32
write("tx-b5-junk-log.json", {"status": "1", "message": "OK", "result": {
    "hash": H5, "blockNumber": "900", "timeStamp": TIME, "from": A, "to": ROUTER, "value": "0", "gasUsed": "1",
    "gasPrice": "1", "success": True, "logs": [
        log(USDT, A, B, 7_000_000, 1),
        {"address": ROUTER, "topics": [TRANSFER, "0x" + "ff" * 32, word(B), None], "data": amount(5), "index": "2"},
        {"address": ROUTER, "topics": [TRANSFER, None, None, None], "data": "0x" + word(A)[2:] + word(B)[2:]
         + amount(5)[2:], "index": "3"},
    ]}})
write("tx-b5-internal.json", {"status": "0", "message": "No internal transactions found", "result": []})
write("tx-no-success.json", {"status": "1", "message": "OK", "result": {
    "hash": H1, "blockNumber": "900", "timeStamp": TIME, "from": A, "to": B, "value": "1", "gasUsed": "1",
    "gasPrice": "1", "logs": []}})
write("gateway-not-found.json", {"message": "Not Found"})
write("tx-unknown.json", {"status": "0", "message": "Transaction not found", "result": None})
write("tx-pending.json", {"status": "1", "message": "OK", "result": {
    "hash": H4, "blockNumber": None, "timeStamp": None, "from": A, "to": B, "value": "3", "gasPrice": "1",
    "success": False, "logs": []}})
write("token-huge.json", {"status": "1", "message": "OK", "result": {
    "contractAddress": FOO, "decimals": "255", "symbol": "JUNK", "name": "Junk", "type": "ERC-20"}})
write("rate-limit.json", {"status": "0", "message": "NOTOK", "result": "Max rate limit reached"})


def entry(tx, frm, to, value, block, **more):
    base = {"hash": tx, "blockNumber": str(block), "blockHash": "0x" + format(block, "064x"), "timeStamp": TIME,
            "from": frm, "to": to, "value": str(value), "gasUsed": "21000", "gasPrice": "1000000000",
            "isError": "0", "txreceipt_status": "1", "contractAddress": ""}
    base.update(more)
    return base


write("history-txs.json", {"status": "1", "message": "OK", "result": [
    entry(H1, A, ROUTER, 10**18, 900),
    entry(H3, B, A, 2 * 10**18, 899),
    entry(H2, A, B, 5, 898, isError="1", txreceipt_status="0"),
]})
write("history-tokens.json", {"status": "1", "message": "OK", "result": [
    entry(H1, A, B, 52_000_000_000, 900, contractAddress=USDT, tokenSymbol="USDT", tokenDecimal="6",
          tokenName="Tether USD"),
    entry(H4, C, A, 3, 897, contractAddress="0x" + "66" * 20, tokenSymbol="", tokenDecimal="",
          tokenName="An NFT"),
    entry(H4, C, A, 3, 897, contractAddress=FOO, tokenSymbol="JUNK", tokenDecimal="255", tokenName="Junk"),
    entry(H3, A, "0x" + "77" * 20, 0, 896, contractAddress=USDT, tokenSymbol="USDT", tokenDecimal="6",
          tokenName="Tether USD"),
]})
write("history-internal.json", {"status": "2", "message": "Some internal transactions have not been processed",
                                "result": [{"transactionHash": H3, "blockNumber": "899", "timeStamp": TIME,
                                            "from": C, "to": A, "value": "9", "isError": "0", "index": "0",
                                            "contractAddress": "", "type": "call"},
                                           {"transactionHash": H1, "blockNumber": "898", "timeStamp": TIME,
                                            "from": A, "to": "", "value": "4", "isError": "1", "index": "0",
                                            "contractAddress": "", "type": "create"}]})
write("history-empty.json", {"status": "0", "message": "No transactions found", "result": []})
write("history-stranger.json", {"status": "1", "message": "OK", "result": [entry(H1, B, C, 1, 900)]})
write("history-full.json", {"status": "1", "message": "OK", "result": [
    entry(H1, A, B, 1, 900), entry(H3, A, B, 1, 899)]})
write("history-pending.json", {"status": "1", "message": "OK", "result": [
    entry(H1, A, B, 1, 900) | {"blockNumber": "", "blockHash": ""}, entry(H3, A, B, 1, 900) | {"blockNumber": ""}]})
write("history-one-block.json", {"status": "1", "message": "OK", "result": [
    entry(H1, A, B, 1, 900), entry(H3, A, B, 1, 900)]})
