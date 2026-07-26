# スキャンの棘を取る。
# 3Dスキャンには、一点だけ外へ飛び出した頂点が必ず残る。
# 面積は小さいのに法線が大きく傾くので、光が当たると白い点として散らばり、
# どれだけ質感を作り込んでも「汚れた模型」に見えてしまう。
import numpy as np
from collections import defaultdict
P='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Models/monden_head.obj'
lines=open(P).read().split('\n')
V=[];VT=[];F=[];other=[]
for L in lines:
    if L.startswith('v '): V.append([float(x) for x in L.split()[1:4]])
    elif L.startswith('vt '): VT.append(L)
    elif L.startswith('f '): F.append(L)
    else: other.append(L)
V=np.array(V)
faces=[]
for L in F:
    idx=[int(t.split('/')[0])-1 for t in L.split()[1:]]
    faces.append(idx)
nb=defaultdict(set)
for f in faces:
    for i in range(len(f)):
        a,b=f[i],f[(i+1)%len(f)]
        nb[a].add(b); nb[b].add(a)
print('頂点%d 面%d'%(len(V),len(faces)))

W=V.copy()
for it in range(6):
    avg=np.zeros_like(W); cnt=np.zeros(len(W))
    for i,s in nb.items():
        if not s: continue
        avg[i]=W[list(s)].mean(0); cnt[i]=1
    d=np.linalg.norm(W-avg,axis=1)
    thr=np.percentile(d[cnt>0],99.0)
    spike=(d>thr)&(cnt>0)
    # 棘だけを近傍の平均へ寄せる。顔のかたちには触らない。
    W[spike]=W[spike]*0.25+avg[spike]*0.75
    print(' 回%d: 棘 %d 個 (しきい値 %.4f)'%(it+1,spike.sum(),thr))

# 全体にごく弱い平滑化（λ=0.12）を一度だけ。細部は残る。
avg=np.zeros_like(W)
for i,s in nb.items():
    if s: avg[i]=W[list(s)].mean(0)
W=W*0.88+avg*0.12
print('移動量 平均%.5f 最大%.5f'%(np.linalg.norm(W-V,axis=1).mean(),np.linalg.norm(W-V,axis=1).max()))

out=[]
vi=0
for L in lines:
    if L.startswith('v '):
        out.append('v %.6f %.6f %.6f'%tuple(W[vi])); vi+=1
    else: out.append(L)
open(P,'w').write('\n'.join(out))
print('保存')
