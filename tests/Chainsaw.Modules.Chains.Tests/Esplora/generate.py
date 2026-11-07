import hashlib, json, os, sys
OUT=sys.argv[1]
CHARSET="qpzry9x8gf2tvdw0s3jn54khce6mua7l"
def polymod(v):
    G=[0x3b6a57b2,0x26508e6d,0x1ea119fa,0x3d4233dd,0x2a1462b3]; c=1
    for x in v:
        b=c>>25; c=(c&0x1ffffff)<<5^x
        for i in range(5): c^=G[i] if (b>>i)&1 else 0
    return c
def hrpx(h): return [ord(x)>>5 for x in h]+[0]+[ord(x)&31 for x in h]
def convertbits(d,f,t):
    acc=bits=0; r=[]; m=(1<<t)-1
    for v in d:
        acc=(acc<<f)|v; bits+=f
        while bits>=t: bits-=t; r.append((acc>>bits)&m)
    if bits: r.append((acc<<(t-bits))&m)
    return r
def segwit(hrp,ver,prog):
    const=1 if ver==0 else 0x2bc830a3
    data=[ver]+convertbits(prog,8,5)
    pm=polymod(hrpx(hrp)+data+[0]*6)^const
    return hrp+'1'+''.join(CHARSET[d] for d in data+[(pm>>5*(5-i))&31 for i in range(6)])
B58="123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz"
def b58check(b):
    b=b+hashlib.sha256(hashlib.sha256(b).digest()).digest()[:4]
    n=int.from_bytes(b,'big'); s=''
    while n: n,r=divmod(n,58); s=B58[r]+s
    return '1'*(len(b)-len(b.lstrip(b'\0')))+s
def h(x,n): return bytes([x])*n
S={}
S['pkh']=('76a914'+h(0x11,20).hex()+'88ac', b58check(b'\x00'+h(0x11,20)), 'p2pkh')
S['sh']=('a914'+h(0x22,20).hex()+'87', b58check(b'\x05'+h(0x22,20)), 'p2sh')
S['wpkh']=('0014'+h(0x33,20).hex(), segwit('bc',0,h(0x33,20)), 'v0_p2wpkh')
S['wsh']=('0020'+h(0x44,32).hex(), segwit('bc',0,h(0x44,32)), 'v0_p2wsh')
S['tr']=('5120'+h(0x55,32).hex(), segwit('bc',1,h(0x55,32)), 'v1_p2tr')
S['opret']=('6a0b68656c6c6f20776f726c64', None, 'op_return')
S['commit']=('6a24aa21a9ed'+h(0x66,32).hex(), None, 'op_return')
def spk(k,v):
    s,a,t=S[k]; d={"scriptpubkey":s,"scriptpubkey_asm":"(asm)","scriptpubkey_type":t}
    if a: d["scriptpubkey_address"]=a
    d["value"]=v; return d
def vin(txid,vout,k,v,seq=4294967293):
    return {"txid":txid,"vout":vout,"prevout":spk(k,v),"scriptsig":"","scriptsig_asm":"","witness":["30"],"is_coinbase":False,"sequence":seq}
H={800000:'00000000000000000001'+'a0'*22, 800001:'00000000000000000001'+'a1'*22, 799000:'00000000000000000001'+'90'*22, 800003:'00000000000000000001'+'a3'*22}
def status(height):
    if height is None: return {"confirmed":False}
    return {"confirmed":True,"block_height":height,"block_hash":H[height],"block_time":1700000000+(height-800000)*600}
def tx(txid,ins,outs,height,fee=None,size=400,weight=1000,version=2,locktime=0):
    if fee is None: fee=sum(i["prevout"]["value"] for i in ins)-sum(o["value"] for o in outs)
    return {"txid":txid,"version":version,"locktime":locktime,"vin":ins,"vout":outs,"size":size,"weight":weight,"sigops":1,"fee":fee,"status":status(height)}
T0='a4'*32; T1='a1'*32; T2='a2'*32; T3='a3'*32
t0=tx(T0,[vin('b0'*32,0,'sh',40_020_000)],[spk('pkh',10_000),spk('wpkh',40_000_000)],799000,size=250,weight=673)
t1=tx(T1,[vin('b1'*32,0,'pkh',60_000_000),vin(T0,1,'wpkh',40_000_000)],[spk('wsh',70_000_000),spk('tr',29_990_000),spk('opret',0)],800000,locktime=799999)
t2=tx(T2,[vin(T1,1,'tr',29_990_000,seq=4294967295)],[spk('pkh',20_000_000),spk('wpkh',9_985_000)],None,size=205,weight=616,version=1)
cb={"txid":"0"*64,"vout":4294967295,"prevout":None,"scriptsig":"03"+"01"*8,"scriptsig_asm":"","witness":["00"*32],"is_coinbase":True,"sequence":4294967295}
t3=tx(T3,[cb],[spk('wpkh',625_000_000+1_234_567),spk('commit',0)],800001,fee=0,size=180,weight=612)
os.makedirs(OUT,exist_ok=True)
def w(name,obj):
    with open(os.path.join(OUT,name),'w') as f: json.dump(obj,f,indent=1)
w('tx-confirmed.json',t1); w('tx-unconfirmed.json',t2); w('tx-coinbase.json',t3); w('tx-older.json',t0)
bad=json.loads(json.dumps(t1)); bad['fee']=9_999; w('tx-bad-fee.json',bad)
bad=json.loads(json.dumps(t1)); bad['vout'][0]['scriptpubkey_address']=S['tr'][1]; w('tx-bad-address.json',bad)
bad=json.loads(json.dumps(t1)); bad['vout'][1]['value']=21_000_000*100_000_000+1; w('tx-bad-amount.json',bad)
w('address-page-first.json',[t2,t3,t1]); w('address-page-after.json',[t0]); w('address-page-empty.json',[])
w('outspends-open.json',[{"spent":False},{"spent":True,"txid":T2,"vin":0,"status":{"confirmed":False}},{"spent":False}])
sp=lambda txid,vin,hgt: {"spent":True,"txid":txid,"vin":vin,"status":status(hgt)}
w('outspends-settled.json',[sp('c1'*32,0,800001),sp(T2,0,800003),sp('c2'*32,3,800001)])
w('block.json',{"id":H[800000],"height":800000,"version":536870912,"timestamp":1700000000,"tx_count":3000,"size":1600000,"weight":3993000,"merkle_root":"5e"*32,"previousblockhash":'00000000000000000001'+'9f'*22,"mediantime":1699998000,"nonce":12345,"bits":386089497,"difficulty":1.0})
with open(os.path.join(OUT,'tip-height.txt'),'w') as f: f.write('800010')
json.dump({k:v[1] for k,v in S.items() if v[1]},open(os.path.join(OUT,'addresses.json'),'w'),indent=1)
# Stage 15: a transaction with its real bytes (so the txid is the double SHA-256 of them), a forged description of
# it, a block with a real header that meets its own (easy) target, and an address summary.
import struct
def cs(n):
    if n<0xfd: return bytes([n])
    if n<=0xffff: return b'\xfd'+struct.pack('<H',n)
    return b'\xfe'+struct.pack('<I',n)
def dsha(b): return hashlib.sha256(hashlib.sha256(b).digest()).digest()
def serialize(version,ins,outs,locktime,witness):
    body=cs(len(ins))+b''.join(bytes.fromhex(t)[::-1]+struct.pack('<I',v)+cs(0)+struct.pack('<I',q) for t,v,q in ins)
    body+=cs(len(outs))+b''.join(struct.pack('<q',val)+cs(len(bytes.fromhex(s)))+bytes.fromhex(s) for s,val in outs)
    base=struct.pack('<i',version)+body+struct.pack('<I',locktime)
    wit=b''.join(cs(len(w))+b''.join(cs(len(i))+i for i in w) for w in witness)
    full=struct.pack('<i',version)+b'\x00\x01'+body+wit+struct.pack('<I',locktime)
    return base,full
H[800004]='00000000000000000001'+'a4'*22
rins=[(T0,1,4294967293)]
routs=[(S['wsh'][0],30_000_000),(S['opret'][0],0),(S['pkh'][0],9_990_000)]
base,full=serialize(2,rins,routs,0,[[bytes(72),bytes(33)]])
TR=dsha(base)[::-1].hex()
treal=tx(TR,[vin(T0,1,'wpkh',40_000_000)],[spk('wsh',30_000_000),spk('opret',0),spk('pkh',9_990_000)],800003,size=len(full),weight=len(base)*3+len(full))
w('tx-real.json',treal)
with open(os.path.join(OUT,'tx-real-hex.txt'),'w') as f: f.write(full.hex())
forged=json.loads(json.dumps(treal)); forged['vout'][2]['value']=9_980_000; forged['fee']+=10_000; w('tx-real-forged.json',forged)
w('outspends-real.json',[sp('c3'*32,0,800004),{"spent":False},sp('c4'*32,1,800004)])
prev='00000000000000000001'+'a4'*22; merkle='5e'*32; bits=0x207fffff; t=1700003000
n=0
while True:
    hdr=struct.pack('<i',536870912)+bytes.fromhex(prev)[::-1]+bytes.fromhex(merkle)[::-1]+struct.pack('<III',t,bits,n)
    bh=dsha(hdr)[::-1].hex()
    if int(bh,16) <= (bits&0x7fffff)<<(8*((bits>>24)-3)): break
    n+=1
w('block-real.json',{"id":bh,"height":800005,"version":536870912,"timestamp":t,"tx_count":2,"size":500,"weight":2000,"merkle_root":merkle,"previousblockhash":prev,"mediantime":t-600,"nonce":n,"bits":bits,"difficulty":0.0})
with open(os.path.join(OUT,'block-real-header.txt'),'w') as f: f.write(hdr.hex())
w('address-summary.json',{"address":S['wpkh'][1],"chain_stats":{"funded_txo_count":3,"funded_txo_sum":49_985_000,"spent_txo_count":1,"spent_txo_sum":40_000_000,"tx_count":3},"mempool_stats":{"funded_txo_count":1,"funded_txo_sum":9_985_000,"spent_txo_count":0,"spent_txo_sum":0,"tx_count":1}})
with open(os.path.join(OUT,'ids.json'),'w') as f: json.dump({"real":TR,"block":bh},f,indent=1)
with open(os.path.join(OUT,'tx-real-base-hex.txt'),'w') as f: f.write(base.hex())
