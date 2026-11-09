"""Writes the synthetic TronGrid responses for the TRON tests: made-up addresses and ids that belong to nobody, in the
shapes TronGrid's documented API answers with (wallet/getblock, wallet/gettransactionbyid,
wallet/gettransactioninfobyid, v1/accounts/:address, v1/accounts/:address/transactions[/trc20]). Only USDT's contract
is real: it is how Chainsaw knows the token."""
import hashlib
import json
import sys

sys.path.insert(0, "../Ethereum")
from keccak import keccak256  # noqa: E402

ALPHABET = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"


def base58check(data):
    data = data + hashlib.sha256(hashlib.sha256(data).digest()).digest()[:4]
    number = int.from_bytes(data, "big")
    text = ""
    while number:
        number, rest = divmod(number, 58)
        text = ALPHABET[rest] + text
    return "1" * (len(data) - len(data.lstrip(b"\0"))) + text


def address(byte):
    return "41" + byte * 20


def b58(hex41):
    return base58check(bytes.fromhex(hex41))


A, B, C, X, FOO, LOOKALIKE = (address(b) for b in ("11", "22", "33", "55", "44", "77"))
USDT = "41a614f803b6fd780986a42c78ec9c7f77e6ded13c"
C1, C2, C3, C4, C5, C6, C7 = (h * 32 for h in ("c1", "c2", "c3", "c4", "c5", "c6", "c7"))
TRANSFER = keccak256(b"Transfer(address,address,uint256)")
TIME = 1793872800000  # 2026-11-01 10:00 UTC, in milliseconds
LATER = TIME + 3600_000  # the source's clock an hour later


def block_id(number):
    return format(number, "016x") + "c9" * 24


def block(number, time):
    return {"blockID": block_id(number), "block_header": {"raw_data": {
        "number": number, "txTrieRoot": "00" * 32, "witness_address": address("99"),
        "parentHash": block_id(number - 1), "version": 30, "timestamp": time},
        "witness_signature": "00" * 65}}


def word(hex41):
    return "0" * 24 + hex41[2:]


def amount(value):
    return format(value, "064x")


def log(contract, frm, to, value):
    return {"address": contract[2:], "topics": [TRANSFER, word(frm), word(to)], "data": amount(value)}


def contract(kind, value):
    return {"parameter": {"value": value, "type_url": "type.googleapis.com/protocol." + kind}, "type": kind}


def transaction(txid, kind, value, result="SUCCESS"):
    tx = {"txID": txid, "raw_data": {"contract": [contract(kind, value)], "ref_block_bytes": "0384",
                                     "ref_block_hash": "00" * 8, "expiration": TIME + 60_000, "timestamp": TIME},
          "signature": ["00" * 65], "raw_data_hex": "00"}
    if result is not None:
        tx["ret"] = [{"contractRet": result}]
    return tx


def entry(txid, kind, value, fee=0, result="SUCCESS", internal=None, number=900):
    item = transaction(txid, kind, value, None)
    item["ret"] = [{"contractRet": result, "fee": fee}]
    item.update({"net_usage": 268, "net_fee": fee, "energy_usage": 0, "energy_fee": 0, "energy_usage_total": 0,
                 "internal_transactions": internal or []})
    if number is not None:
        item.update({"blockNumber": number, "block_timestamp": TIME})
    return item


def write(name, value):
    with open(name, "w", encoding="utf-8") as file:
        json.dump(value, file, indent=1)
        file.write("\n")


write("tip.json", block(1000, TIME + 300_000))
write("tip-shallow.json", block(910, TIME + 30_000))
write("block-900.json", block(900, TIME))
write("block-wrong.json", block(901, TIME + 3000))
write("not-found.json", {})
write("rate-limit.json", {"Error": "request rate exceeded the allowed_rps(3), and the query server is suspended for 3s"})

write("tx-c1.json", transaction(C1, "TriggerSmartContract", {
    "data": "23b872dd", "owner_address": A, "contract_address": X}))
write("tx-c1-info.json", {
    "id": C1, "fee": 345000, "blockNumber": 900, "blockTimeStamp": TIME, "contractResult": [""],
    "contract_address": X, "receipt": {"energy_usage_total": 64285, "net_fee": 345000, "result": "SUCCESS"},
    "log": [
        log(USDT, A, B, 52_000_000_000),
        log(FOO, B, C, 5),
        {"address": X[2:], "topics": ["ab" * 32], "data": ""},
        log(USDT, A, LOOKALIKE, 0),  # a transfer of zero to a lookalike: address poisoning
    ],
    "internal_transactions": [
        {"hash": "d1" * 32, "caller_address": X, "transferTo_address": C,
         "callValueInfo": [{"callValue": 2_000_000}], "note": "63616c6c"},
        {"hash": "d2" * 32, "caller_address": X, "transferTo_address": B,
         "callValueInfo": [{"callValue": 7}], "note": "63616c6c", "rejected": True},
        {"hash": "d3" * 32, "caller_address": X, "transferTo_address": B, "callValueInfo": [{}], "note": "63616c6c"},
    ]})
# The same info from an endpoint that has not indexed the events: the block is there, the logs are not.
write("tx-c1-info-nolog.json", {
    "id": C1, "fee": 345000, "blockNumber": 900, "blockTimeStamp": TIME, "contractResult": [""],
    "contract_address": X, "receipt": {"energy_usage_total": 64285, "net_fee": 345000, "result": "SUCCESS"},
    "log": []})
write("tx-c2.json", transaction(C2, "TransferContract", {"amount": 1_000_000, "owner_address": A, "to_address": B}))
write("tx-c2-info.json", {"id": C2, "blockNumber": 900, "blockTimeStamp": TIME, "contractResult": [""],
                          "receipt": {"net_usage": 268}})
write("tx-c3.json", transaction(C3, "TransferContract", {"amount": 9, "owner_address": A, "to_address": B}, None))
write("tx-c4.json", transaction(C4, "TriggerSmartContract", {
    "data": "00", "owner_address": A, "contract_address": X, "call_value": 5_000_000}, "REVERT"))
write("tx-c4-info.json", {"id": C4, "fee": 1_000_000, "blockNumber": 900, "blockTimeStamp": TIME,
                          "contractResult": [""], "receipt": {"energy_fee": 1_000_000, "result": "REVERT"}})

write("history-txs.json", {"data": [
    entry(C2, "TransferContract", {"amount": 1_000_000, "owner_address": A, "to_address": B}),
    entry(C4, "TriggerSmartContract", {"data": "00", "owner_address": A, "contract_address": X,
                                       "call_value": 5_000_000}, fee=1_000_000, result="REVERT"),
    entry(C5, "TransferAssetContract", {"amount": 10, "asset_name": "31303030303031", "owner_address": A,
                                        "to_address": B}),
    entry(C6, "TriggerSmartContract", {"data": "00", "owner_address": B, "contract_address": X}, internal=[
        {"internal_tx_id": "e1" * 32, "data": {"note": "call", "rejected": False, "call_value": {"_": 3_000_000}},
         "to_address": A, "from_address": X},
        {"internal_tx_id": "e2" * 32, "data": {"note": "call", "rejected": True, "call_value": {"_": 4}},
         "to_address": A, "from_address": X},
    ]),
    entry(C7, "TransferContract", {"amount": 9, "owner_address": A, "to_address": B}, number=None),
], "success": True, "meta": {"at": LATER, "page_size": 5}})

write("history-trc20.json", {"data": [
    {"transaction_id": C1, "token_info": {"symbol": "USDT", "address": b58(USDT), "decimals": 6, "name": "Tether USD"},
     "block_timestamp": TIME, "from": b58(A), "to": b58(B), "type": "Transfer", "value": "52000000000"},
    {"transaction_id": C1, "token_info": {"symbol": "USDT", "address": b58(USDT), "decimals": 6, "name": "Tether USD"},
     "block_timestamp": TIME, "from": b58(A), "to": b58(LOOKALIKE), "type": "Transfer", "value": "0"},
    {"transaction_id": C2, "token_info": {"symbol": "NEW", "address": b58(FOO), "name": "Unread"},
     "block_timestamp": TIME, "from": b58(B), "to": b58(A), "type": "Transfer", "value": "3"},
], "success": True, "meta": {"at": LATER, "page_size": 3}})

write("history-stranger.json", {"data": [
    {"transaction_id": C1, "token_info": {"symbol": "USDT", "address": b58(USDT), "decimals": 6, "name": "Tether USD"},
     "block_timestamp": TIME, "from": b58(B), "to": b58(C), "type": "Transfer", "value": "1"},
], "success": True, "meta": {"at": LATER, "page_size": 1}})

# A scam token whose event is not a standard Transfer (two topics, no amount), and a real USDT movement after it.
C8 = "c8" * 32
write("tx-c8-info.json", {
    "id": C8, "fee": 0, "blockNumber": 900, "blockTimeStamp": TIME, "contractResult": [""],
    "receipt": {"result": "SUCCESS"},
    "log": [{"address": FOO[2:], "topics": [TRANSFER, word(FOO)], "data": ""}]})
write("history-scam.json", {"data": [
    {"transaction_id": C8, "token_info": {"symbol": "USDT", "address": b58(FOO), "decimals": 6, "name": "Tether USD"},
     "block_timestamp": TIME, "from": b58(FOO), "to": b58(A), "type": "Transfer", "value": "5000000"},
    {"transaction_id": C1, "token_info": {"symbol": "USDT", "address": b58(USDT), "decimals": 6, "name": "Tether USD"},
     "block_timestamp": TIME, "from": b58(A), "to": b58(B), "type": "Transfer", "value": "52000000000"},
], "success": True, "meta": {"at": LATER, "page_size": 2}})

write("account.json", {"data": [{"address": A, "balance": 5_000_000, "create_time": TIME,
                                 "trc20": [{b58(USDT): "1000000"}, {b58(FOO): "5"}]}],
                       "success": True, "meta": {"at": LATER, "page_size": 1}})
write("account-unused.json", {"data": [], "success": True, "meta": {"at": LATER, "page_size": 0}})
