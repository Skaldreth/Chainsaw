"""Writes the synthetic Solana JSON-RPC answers for the Solana tests: made-up addresses, signatures and hashes that
belong to nobody, in the shapes Solana's documented JSON-RPC answers with (getSlot, getGenesisHash, getBlock with
transactionDetails none, getSignaturesForAddress, getTransaction with encoding jsonParsed, getBalance and
getTokenAccountsByOwner with jsonParsed). Only the mints of USDT, USDC and wrapped SOL and the System and Token programs
are real: they are how Chainsaw knows the tokens and how a transaction names the programs."""
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
TA_A2, PAYER, MULE, TA_A_USDC, POOL_USDC, POOL_WSOL, TEMP = (key(b) for b in (0x58, 0x12, 0x13, 0x57, 0x45, 0x46, 0x47))
USDT = "Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB"
USDC = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v"
WSOL = "So11111111111111111111111111111111111111112"
TOKEN_PROGRAM = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA"
SYSTEM = "11111111111111111111111111111111"
UNKNOWN_PROGRAM = key(0x88)
S1, S2, S3, S4, S5, S6, S7, S8, S9, S10, S11 = (
    signature(b) for b in (0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB))
BLOCKHASH = key(0x99)
PARENT = key(0x98)
TIME = 1793872800  # 2026-11-01 10:00 UTC
SLOT = 400_000_000
FEE = 5000
SOL = 1_000_000_000
RENT = 2_039_280


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


def parsed(program, program_id, kind, info, height=1):
    return {"parsed": {"info": info, "type": kind}, "program": program, "programId": program_id,
            "stackHeight": height}


def system_transfer(source, destination, lamports, height=1):
    return parsed("system", SYSTEM, "transfer",
                  {"destination": destination, "lamports": lamports, "source": source}, height)


def token_ix(kind, info, height=1):
    return parsed("spl-token", TOKEN_PROGRAM, kind, info, height)


def checked(source, destination, mint, amount, authority, decimals=6, height=1):
    return token_ix("transferChecked", {
        "authority": authority, "destination": destination, "mint": mint, "source": source,
        "tokenAmount": {"amount": str(amount), "decimals": decimals}}, height)


def unparsed(accounts):
    return {"accounts": accounts, "data": "3Bxs4ffTu9T19DNF", "programId": UNKNOWN_PROGRAM, "stackHeight": 1}


def transaction(sig, slot, index, keys, pre, post, pre_tokens=(), post_tokens=(), err=None, instructions=(),
                inner=(), version="legacy", signers=1):
    message = {
        "accountKeys": [{"pubkey": k, "signer": i < signers, "source": "transaction", "writable": True}
                        for i, k in enumerate(keys)],
        "instructions": list(instructions), "recentBlockhash": PARENT,
    }
    if version != "legacy":
        message["transactionConfig"] = {}
    return answer({
        "blockTime": TIME + (slot - SLOT) // 2,
        "meta": {
            "computeUnitsConsumed": 450, "err": err, "fee": FEE,
            "innerInstructions": [{"index": i, "instructions": list(group)} for i, group in inner],
            "logMessages": [], "postBalances": post, "postTokenBalances": list(post_tokens), "preBalances": pre,
            "preTokenBalances": list(pre_tokens), "rewards": [],
            "status": {"Err": err} if err else {"Ok": None},
        },
        "slot": slot,
        "transaction": {"message": message, "signatures": [sig]},
        "transactionIndex": index,
        "version": version,
    })


def entry(sig, slot, err=None):
    return {"blockTime": TIME + (slot - SLOT) // 2, "confirmationStatus": "finalized", "err": err, "memo": None,
            "signature": sig, "slot": slot, "transactionIndex": 3}


FAILED = {"InstructionError": [0, {"Custom": 1}]}

# S1: A sends 1.5 SOL to B.
write("tx-s1.json", transaction(
    S1, SLOT, 12, [A, B, SYSTEM], [10 * SOL, SOL, 1], [10 * SOL - 1_500_000_000 - FEE, SOL + 1_500_000_000, 1],
    instructions=[system_transfer(A, B, 1_500_000_000)]))
# S2: A sends 250 USDT from its token account to B's; A pays the fee.
write("tx-s2.json", transaction(
    S2, SLOT + 10, 4, [A, TA_A, TA_B, TOKEN_PROGRAM], [SOL, RENT, RENT, 1], [SOL - FEE, RENT, RENT, 1],
    [token(1, USDT, A, 1_000_000_000), token(2, USDT, B, 0)],
    [token(1, USDT, A, 750_000_000), token(2, USDT, B, 250_000_000)],
    instructions=[checked(TA_A, TA_B, USDT, 250_000_000, A)]))
# S3: A's transaction fails; only the fee moves.
write("tx-s3.json", transaction(
    S3, SLOT + 20, 0, [A, B, SYSTEM], [SOL, SOL, 1], [SOL - FEE, SOL, 1], err=FAILED,
    instructions=[system_transfer(A, B, SOL)]))
# S4: A and B both pay, and C and the pool both receive, through a program the node does not parse: the instructions
# explain nothing of it, so nothing is shown and it is said.
write("tx-s4.json", transaction(
    S4, SLOT + 30, 1, [A, B, C, POOL, UNKNOWN_PROGRAM], [10 * SOL, 10 * SOL, 0, 0, 1],
    [9 * SOL - FEE, 8 * SOL, 1_500_000_000, 1_500_000_000, 1], signers=2,
    instructions=[unparsed([A, B, C, POOL])]))
# S5: B's new token account is made (A pays its rent) and 5 FOO (a token nobody knows, 0 decimals) are created in it.
write("tx-s5.json", transaction(
    S5, SLOT + 40, 2, [A, TA_B, FOO, TOKEN_PROGRAM, SYSTEM], [SOL, 0, 1_461_600, 1, 1],
    [SOL - FEE - RENT, RENT, 1_461_600, 1, 1], [], [token(1, FOO, B, 5, 0)],
    instructions=[
        parsed("system", SYSTEM, "createAccount",
               {"lamports": RENT, "newAccount": TA_B, "owner": TOKEN_PROGRAM, "source": A, "space": 165}),
        token_ix("initializeAccount3", {"account": TA_B, "mint": FOO, "owner": B}),
        token_ix("mintTo", {"account": TA_B, "amount": "5", "mint": FOO, "mintAuthority": A}),
    ]))
# S6: A swaps 100 USDC for 0.5 SOL at a pool: the USDC goes to the pool owner's token account, the wrapped SOL comes
# out of the pool's into a temporary account of A's made and closed in the transaction, which hands it to A.
write("tx-s6.json", transaction(
    S6, SLOT + 50, 7, [A, TA_A_USDC, POOL_USDC, POOL_WSOL, TEMP, TOKEN_PROGRAM, SYSTEM, UNKNOWN_PROGRAM],
    [SOL, RENT, RENT, 50 * SOL + RENT, 0, 1, 1, 1],
    [SOL - FEE + SOL // 2, RENT, RENT, 50 * SOL - SOL // 2 + RENT, 0, 1, 1, 1],
    [token(1, USDC, A, 100_000_000), token(2, USDC, POOL, 900_000_000), token(3, WSOL, POOL, 50 * SOL, 9)],
    [token(1, USDC, A, 0), token(2, USDC, POOL, 1_000_000_000), token(3, WSOL, POOL, 50 * SOL - SOL // 2, 9)],
    instructions=[
        parsed("system", SYSTEM, "createAccount",
               {"lamports": RENT, "newAccount": TEMP, "owner": TOKEN_PROGRAM, "source": A, "space": 165}),
        token_ix("initializeAccount3", {"account": TEMP, "mint": WSOL, "owner": A}),
        unparsed([A, TA_A_USDC, POOL_USDC, POOL_WSOL, TEMP]),
        token_ix("closeAccount", {"account": TEMP, "destination": A, "owner": A}),
    ],
    inner=[(2, [checked(TA_A_USDC, POOL_USDC, USDC, 100_000_000, A, height=2),
                checked(POOL_WSOL, TEMP, WSOL, SOL // 2, POOL, decimals=9, height=2)])]))
# S7 (version 1): A sends 1 SOL to a mule, who sends it on to C in the same transaction.
write("tx-s7.json", transaction(
    S7, SLOT + 60, 3, [A, MULE, C, SYSTEM], [5 * SOL, SOL, 0, 1], [4 * SOL - FEE, SOL, SOL, 1], signers=2,
    instructions=[system_transfer(A, MULE, SOL), system_transfer(MULE, C, SOL)], version=1))
# S8: a payer pays the fee for A, who sends 1 SOL to B.
write("tx-s8.json", transaction(
    S8, SLOT + 70, 5, [PAYER, A, B, SYSTEM], [SOL, 3 * SOL, 0, 1], [SOL - FEE, 2 * SOL, SOL, 1], signers=2,
    instructions=[system_transfer(A, B, SOL)]))
# S9: A closes its empty USDT account into C and sends 1 SOL to B.
write("tx-s9.json", transaction(
    S9, SLOT + 80, 6, [A, TA_A, B, C, TOKEN_PROGRAM, SYSTEM], [5 * SOL, RENT, 0, 0, 1, 1],
    [4 * SOL - FEE, 0, SOL, RENT, 1, 1], [token(1, USDT, A, 0)], [],
    instructions=[token_ix("closeAccount", {"account": TA_A, "destination": C, "owner": A}),
                  system_transfer(A, B, SOL)]))
# S10: A burns 10 USDT.
write("tx-s10.json", transaction(
    S10, SLOT + 90, 2, [A, TA_A, USDT, TOKEN_PROGRAM], [SOL, RENT, 1, 1], [SOL - FEE, RENT, 1, 1],
    [token(1, USDT, A, 750_000_000)], [token(1, USDT, A, 740_000_000)],
    instructions=[token_ix("burnChecked", {"account": TA_A, "authority": A, "mint": USDT,
                                           "tokenAmount": {"amount": "10000000", "decimals": 6}})]))
# S11: A sends 5 USDT from its second USDT account to B's.
write("tx-s11.json", transaction(
    S11, SLOT, 9, [A, TA_A2, TA_B, TOKEN_PROGRAM], [SOL, RENT, RENT, 1], [SOL - FEE, RENT, RENT, 1],
    [token(1, USDT, A, 5_000_000), token(2, USDT, B, 0)], [token(1, USDT, A, 0), token(2, USDT, B, 5_000_000)],
    instructions=[checked(TA_A2, TA_B, USDT, 5_000_000, A)]))

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
write("signatures-token-account.json", answer([entry(S2, SLOT + 10)]))
write("signatures-x-newest.json", answer([entry(S10, SLOT + 90), entry(S2, SLOT + 10)]))
write("signatures-x-older.json", answer([entry(S2, SLOT + 10)]))
write("signatures-y-newest.json", answer([entry(S11, SLOT)]))
write("balance.json", answer({"context": {"apiVersion": "4.4.0", "slot": SLOT + 100}, "value": 3 * SOL}))


def holding(account, mint, owner, amount, decimals=6):
    return {"pubkey": account, "account": {"lamports": RENT, "data": {"program": "spl-token", "parsed": {"info": {
        "isNative": False, "mint": mint, "owner": owner, "state": "initialized",
        "tokenAmount": {"amount": str(amount), "decimals": decimals, "uiAmount": amount / 10 ** decimals,
                        "uiAmountString": str(amount / 10 ** decimals)}},
        "type": "account"}, "space": 165}, "owner": TOKEN_PROGRAM, "executable": False, "space": 165}}


def holdings(*accounts):
    return answer({"context": {"apiVersion": "4.4.0", "slot": SLOT + 100}, "value": list(accounts)})


write("token-accounts.json", holdings(holding(TA_A, USDT, A, 750_000_000)))
write("token-accounts-two.json", holdings(holding(TA_A, USDT, A, 740_000_000), holding(TA_A2, USDT, A, 0)))
write("token-accounts-none.json", holdings())
