"""Writes the synthetic Solana JSON-RPC answers for the Solana tests: made-up addresses, signatures and hashes that
belong to nobody, in the shapes Solana's documented JSON-RPC answers with (getSlot, getGenesisHash, getBlock with
transactionDetails none, getSignaturesForAddress, getTransaction with encoding json, getBalance and
getTokenAccountsByOwner with jsonParsed). Only USDT's mint and the Token and System programs are real: they are how
Chainsaw knows the token and how a transaction names the programs."""
import json

ALPHABET = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"


def b58(data):
    number = int.from_bytes(data, "big")
    text = ""
    while number:
        number, rest = divmod(number, 58)
        text = ALPHABET[rest] + text
    return "1" * (len(data) - len(data.lstrip(b"\0"))) + text


def key(byte):
    return b58(bytes([byte]) * 32)


def signature(byte):
    return b58(bytes([byte]) * 64)


A, B, C, POOL, TA_A, TA_B, FOO = (key(b) for b in (0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77))
USDT = "Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB"
TOKEN_PROGRAM = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA"
SYSTEM = "11111111111111111111111111111111"
S1, S2, S3, S4, S5 = (signature(b) for b in (0xA1, 0xA2, 0xA3, 0xA4, 0xA5))
BLOCKHASH = key(0x99)
PARENT = key(0x98)
TIME = 1793872800  # 2026-11-01 10:00 UTC
SLOT = 400_000_000
FEE = 5000
SOL = 1_000_000_000


def answer(result):
    return {"jsonrpc": "2.0", "result": result, "id": 1}


def write(name, value):
    with open(name, "w", encoding="utf-8") as file:
        json.dump(value, file, indent=2)
        file.write("\n")


def token(index, mint, owner, amount, decimals=6):
    return {
        "accountIndex": index, "mint": mint, "owner": owner, "programId": TOKEN_PROGRAM,
        "uiTokenAmount": {"amount": str(amount), "decimals": decimals, "uiAmount": amount / 10 ** decimals,
                          "uiAmountString": str(amount / 10 ** decimals)},
    }


def transaction(sig, slot, index, keys, pre, post, pre_tokens=(), post_tokens=(), err=None, loaded=None):
    return answer({
        "blockTime": TIME + (slot - SLOT) // 2,
        "meta": {
            "computeUnitsConsumed": 450, "err": err, "fee": FEE, "innerInstructions": [],
            "loadedAddresses": loaded or {"readonly": [], "writable": []}, "logMessages": [],
            "postBalances": post, "postTokenBalances": list(post_tokens), "preBalances": pre,
            "preTokenBalances": list(pre_tokens), "rewards": [],
            "status": {"Err": err} if err else {"Ok": None},
        },
        "slot": slot,
        "transaction": {
            "message": {
                "accountKeys": keys,
                "header": {"numReadonlySignedAccounts": 0, "numReadonlyUnsignedAccounts": 1,
                           "numRequiredSignatures": 1},
                "instructions": [], "recentBlockhash": PARENT,
            },
            "signatures": [sig],
        },
        "transactionIndex": index,
        "version": "legacy",
    })


def entry(sig, slot, err=None):
    return {"blockTime": TIME + (slot - SLOT) // 2, "confirmationStatus": "finalized", "err": err, "memo": None,
            "signature": sig, "slot": slot, "transactionIndex": 3}


FAILED = {"InstructionError": [0, {"Custom": 1}]}

# S1: A sends 1.5 SOL to B.
write("tx-s1.json", transaction(
    S1, SLOT, 12, [A, B, SYSTEM], [10 * SOL, SOL, 1], [10 * SOL - 1_500_000_000 - FEE, SOL + 1_500_000_000, 1]))
# S2: A sends 250 USDT from its token account to B's; A pays the fee.
write("tx-s2.json", transaction(
    S2, SLOT + 10, 4, [A, TA_A, TA_B, TOKEN_PROGRAM], [SOL, 2_039_280, 2_039_280, 1],
    [SOL - FEE, 2_039_280, 2_039_280, 1],
    [token(1, USDT, A, 1_000_000_000), token(2, USDT, B, 0)],
    [token(1, USDT, A, 750_000_000), token(2, USDT, B, 250_000_000)]))
# S3: A's transaction fails; only the fee moves.
write("tx-s3.json", transaction(S3, SLOT + 20, 0, [A, B, SYSTEM], [SOL, SOL, 1], [SOL - FEE, SOL, 1], err=FAILED))
# S4: A and B both pay, C and the pool both receive: the balances cannot say who paid whom. C and the pool are
# loaded from an address lookup table.
write("tx-s4.json", transaction(
    S4, SLOT + 30, 1, [A, B, SYSTEM], [10 * SOL, 10 * SOL, 1, 0, 0],
    [9 * SOL - FEE, 8 * SOL, 1, 1_500_000_000, 1_500_000_000],
    loaded={"writable": [C, POOL], "readonly": []}))
# S5: 5 FOO (a token nobody knows, 0 decimals) are created in B's new token account; A pays.
write("tx-s5.json", transaction(
    S5, SLOT + 40, 2, [A, TA_B, FOO, TOKEN_PROGRAM], [SOL, 0, 1_461_600, 1],
    [SOL - FEE - 2_039_280, 2_039_280, 1_461_600, 1], [], [token(1, FOO, B, 5, 0)]))

write("block.json", answer({"blockHeight": 380_000_000, "blockTime": TIME, "blockhash": BLOCKHASH,
                            "parentSlot": SLOT - 1, "previousBlockhash": PARENT}))
write("block-skipped.json", {"jsonrpc": "2.0", "error": {
    "code": -32007, "message": f"Slot {SLOT + 1} was skipped, or missing due to ledger jump to recent snapshot"},
    "id": 1})
write("tip.json", answer(SLOT + 100))
write("genesis.json", answer("5eykt4UsFv8P8NJdTREpY1vzqKqZKvdpKuc147dw2N9d"))
write("not-found.json", answer(None))
write("rate-limit.json", {"jsonrpc": "2.0", "error": {"code": 429, "message": "Too many requests"}, "id": 1})
write("signatures-newest.json", answer([entry(S4, SLOT + 30), entry(S3, SLOT + 20, FAILED)]))
write("signatures-older.json", answer([entry(S2, SLOT + 10), entry(S1, SLOT)]))
write("signatures-all.json", answer(
    [entry(S4, SLOT + 30), entry(S3, SLOT + 20, FAILED), entry(S2, SLOT + 10), entry(S1, SLOT)]))
write("signatures-token-account.json", answer([entry(S2, SLOT + 10)]))
write("balance.json", answer({"context": {"apiVersion": "4.4.0", "slot": SLOT + 100}, "value": 3 * SOL}))
write("token-accounts.json", answer({"context": {"apiVersion": "4.4.0", "slot": SLOT + 100}, "value": [
    {"pubkey": TA_A, "account": {"lamports": 2_039_280, "data": {"program": "spl-token", "parsed": {"info": {
        "isNative": False, "mint": USDT, "owner": A, "state": "initialized",
        "tokenAmount": {"amount": "750000000", "decimals": 6, "uiAmount": 750.0, "uiAmountString": "750"}},
        "type": "account"}, "space": 165}, "owner": TOKEN_PROGRAM, "executable": False, "space": 165}},
]}))
write("token-accounts-none.json", answer({"context": {"apiVersion": "4.4.0", "slot": SLOT + 100}, "value": []}))

# S6: A swaps 100 USDC for 0.5 SOL at a pool: the USDC goes into the pool owner's token account, and the SOL comes
# out of the pool's wrapped-SOL token account, whose lamports are its owner's.
USDC = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v"
WSOL = "So11111111111111111111111111111111111111112"
TA_A_USDC, POOL_USDC, POOL_WSOL = (key(b) for b in (0x57, 0x45, 0x46))
S6 = signature(0xA6)
write("tx-s6.json", transaction(
    S6, SLOT + 50, 7, [A, TA_A_USDC, POOL_USDC, POOL_WSOL, TOKEN_PROGRAM],
    [SOL, 2_039_280, 2_039_280, 50 * SOL + 2_039_280, 1],
    [SOL - FEE + SOL // 2, 2_039_280, 2_039_280, 50 * SOL - SOL // 2 + 2_039_280, 1],
    [token(1, USDC, A, 100_000_000), token(2, USDC, POOL, 900_000_000), token(3, WSOL, POOL, 50 * SOL, 9)],
    [token(1, USDC, A, 0), token(2, USDC, POOL, 1_000_000_000), token(3, WSOL, POOL, 50 * SOL - SOL // 2, 9)]))
