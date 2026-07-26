import numpy as np, struct
from collections import defaultdict, deque

OUT='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Models/monden_glasses.obj'

src='/sessions/youthful-festive-cannon/mnt/uploads/monden_head_noneck.stl'
buf=open(src,'rb').read(); n=struct.unpack('<I',buf[80:84])[0]
dd=np.frombuffer(buf,dtype=np.uint8,count=n*50,offset=84).reshape(n,50)
tris=dd[:,12:48].copy().view(np.float32).reshape(n,3,3).astype(np.float64)
V0=tris.reshape(-1,3)
uq,inv=np.unique(np.round(V0,3),axis=0,return_inverse=True)
V=np.zeros_like(uq); c=np.zeros(len(uq)); np.add.at(V,inv,V0); np.add.at(c,inv,1); V/=c[:,None]
Q=np.stack([V[:,0],V[:,2],-V[:,1]],1)
ctr=(Q.max(0)+Q.min(0))/2; scl=(Q.max(0)-Q.min(0)).max()
Q=(Q-ctr)/scl
F=inv.reshape(n,3)
print('頂点 %d / 面 %d' % (len(Q), len(F)))

r=np.linalg.norm(Q,axis=1); d=Q/r[:,None]
u=(np.arctan2(d[:,0],d[:,2])/(2*np.pi))+0.5
v=np.arcsin(np.clip(d[:,1],-1,1))/np.pi+0.5
AU,AV=512,256
key=np.clip((v*AV).astype(int),0,AV-1)*AU+np.clip((u*AU).astype(int),0,AU-1)
inner=np.full(AU*AV,np.inf); np.minimum.at(inner,key,r)
cand=np.where((r-inner[key])>0.010)[0]

cell=0.012
cells=defaultdict(list)
for i,g in enumerate(map(tuple,np.floor(Q[cand]/cell).astype(int))): cells[g].append(i)
seen=set(); best=None
for c0 in cells:
    if c0 in seen: continue
    q=deque([c0]); seen.add(c0); mem=[]
    while q:
        cc=q.popleft(); mem.extend(cells[cc])
        a,b,e=cc
        for da in(-1,0,1):
            for db in(-1,0,1):
                for de in(-1,0,1):
                    nb=(a+da,b+db,e+de)
                    if nb in cells and nb not in seen: seen.add(nb); q.append(nb)
    if best is None or len(mem)>len(best): best=mem
fidx=cand[np.array(best)]
print('フレーム候補 %d点' % len(fidx))

mark=np.zeros(len(Q),bool); mark[fidx]=True
for it in range(2):
    hit=mark[F].sum(1)>=2
    mark[F[hit].ravel()]=True
sel=mark[F].all(1)
print('採った面 %d' % sel.sum())

FS=Q[fidx]
lo=FS.min(0)-0.02; hi=FS.max(0)+0.02
cen=Q[F[sel]].mean(1)
keep=np.all((cen>lo)&(cen<hi),axis=1)
keep&= cen[:,2] > FS[:,2].min()-0.02
faces=F[sel][keep]
print('残した面 %d' % len(faces))

adj=defaultdict(list)
for i,f in enumerate(faces):
    for a in f: adj[a].append(i)
seenf=np.zeros(len(faces),bool); groups=[]
for s in range(len(faces)):
    if seenf[s]: continue
    q=deque([s]); seenf[s]=True; g=[s]
    while q:
        i=q.popleft()
        for a in faces[i]:
            for j in adj[a]:
                if not seenf[j]: seenf[j]=True; q.append(j); g.append(j)
    groups.append(g)
groups.sort(key=len,reverse=True)
print('kata: '+', '.join(str(len(g)) for g in groups[:6]))
faces=faces[np.array([i for g in groups if len(g)>=max(200,len(groups[0])//12) for i in g])]
print('最終 %d面' % len(faces))

used=np.unique(faces)
remap=-np.ones(len(Q),int); remap[used]=np.arange(len(used))
P=Q[used]
nrm=P/np.linalg.norm(P,axis=1,keepdims=True)
P=P+nrm*0.004

with open(OUT,'w') as f:
    f.write('# monden glasses frame (extracted from scan)\n')
    for p in P: f.write('v %.6f %.6f %.6f\n' % tuple(p))
    for t in faces: f.write('f %d %d %d\n' % tuple(remap[t]+1))
print('保存: %s  頂点%d 面%d' % (OUT,len(P),len(faces)))
print('範囲 x %.3f..%.3f  y %.3f..%.3f  z %.3f..%.3f' % (
    P[:,0].min(),P[:,0].max(),P[:,1].min(),P[:,1].max(),P[:,2].min(),P[:,2].max()))
