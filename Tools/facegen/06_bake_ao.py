# 法線マップと AO マップを焼く。
#
# 球面UVなので、頭の表面は「向き(u,v) → 中心からの距離 r」という地形図になる。
# 地形図があれば、傾きから法線が、地平線の高さから AO が求まる。
# レイトレーサを書かずに済むのは、UVの張り方がこの形だからこそ。
import numpy as np, time
from PIL import Image
from scipy import ndimage
T0=time.time()
def log(*a): print('[%5.1fs]'%(time.time()-T0), *a, flush=True)

W,H=2048,1024
R_hi=np.load('/tmp/R_hi.npy'); R_lo=np.load('/tmp/R_lo.npy')
v=1-(np.arange(H)+0.5)/H
cosphi=np.maximum(np.cos((v-0.5)*np.pi),0.15)[:,None]

def slopes(R):
    dRdx=np.gradient(R,axis=1)*W
    dRdy=-np.gradient(R,axis=0)*H
    return dRdx/(2*np.pi*R*cosphi), dRdy/(np.pi*R)

gx_hi,gy_hi=slopes(ndimage.gaussian_filter(R_hi,0.8))
gx_lo,gy_lo=slopes(R_lo)

# ---------- 法線マップ ----------
# 間引きで失われた分だけを記録する。低解像度メッシュ自身の傾きは
# Unity 側が持っているので、差だけを渡せばいい。
nx=-(gx_hi-gx_lo); ny=-(gy_hi-gy_lo)

# 肌理（毛穴と産毛）。実際の肌が均一に見えない最大の理由は、この細かさ。
rng=np.random.default_rng(7)
def fine(f,amp):
    g=rng.normal(0,1,(H//f,W//f))
    g=np.array(Image.fromarray(((g-g.min())/(np.ptp(g))*255).astype(np.uint8)).resize((W,H),Image.BICUBIC))/255.0-0.5
    return g*amp
pore=fine(2,1.0)*0.55+fine(4,1.0)*0.30+fine(8,1.0)*0.15
# 毛穴は額・鼻・頬に多く、唇と目のまわりには少ない
uu=(np.arange(W)+0.5)/W
UU,VV=np.meshgrid(uu,v)
face=np.clip((np.cos((UU-0.5)*2*np.pi)-0.2)/0.8,0,1)
pore_amt=face*np.clip(1-np.abs(VV-0.55)/0.35,0,1)
px_,py_=np.gradient(pore*pore_amt)
nx+= py_*W*0.00055
ny+=-px_*H*0.00055

nz=np.ones_like(nx)
L=np.sqrt(nx**2+ny**2+nz**2)
nrm=np.stack([nx/L,ny/L,nz/L],-1)
Image.fromarray(((nrm*0.5+0.5)*255).astype(np.uint8)).save('/tmp/normal.png')
log('法線マップ 傾きの大きさ 平均%.3f 最大%.3f'%(np.hypot(nx,ny).mean(),np.hypot(nx,ny).max()))

# ---------- AO（環境遮蔽）----------
# 地形図の上で、各点から周囲を見渡して地平線の高さを測る。
# 地平線が高いほど空が狭く、届く光が少ない＝暗い。
h=1024//2, 2048//2
def resize(A,w,hh): return np.array(Image.fromarray(A.astype(np.float32),mode='F').resize((w,hh),Image.BILINEAR))
Wa,Ha=1024,512
Ra=resize(R_hi,Wa,Ha)
va=1-(np.arange(Ha)+0.5)/Ha
cpa=np.maximum(np.cos((va-0.5)*np.pi),0.15)[:,None]
gxa=np.gradient(Ra,axis=1)*Wa/(2*np.pi*Ra*cpa)
gya=-np.gradient(Ra,axis=0)*Ha/(np.pi*Ra)

K=10; horizon=np.zeros((K,Ha,Wa))
steps=np.unique(np.round(np.geomspace(1.5,48,20)).astype(int))
for k in range(K):
    th=2*np.pi*k/K; ct,st=np.cos(th),np.sin(th)
    best=np.full((Ha,Wa),-1e9)
    for s in steps:
        dx=int(round(ct*s)); dy=int(round(st*s))
        if dx==0 and dy==0: continue
        Rs=np.roll(np.roll(Ra,-dx,axis=1),-dy,axis=0)
        # 弧長での距離
        wx=dx/Wa*2*np.pi*Ra*cpa; wy=dy/Ha*np.pi*Ra
        dist=np.hypot(wx,wy)
        tangent=gxa*wx - gya*wy          # 行は下ほど v が小さいので符号を反転
        rel=(Rs-Ra-tangent)/np.maximum(dist,1e-6)
        best=np.maximum(best,rel)
    horizon[k]=best
sin_h=np.clip(horizon,0,None)/np.sqrt(1+np.clip(horizon,0,None)**2)
ao=1.0-sin_h.mean(0)
ao=np.clip(ao,0,1)
ao=ndimage.gaussian_filter(ao,1.2)
ao=np.clip((ao-ao.min())/(ao.max()-ao.min()),0,1)
ao=0.30+0.70*ao                       # 真っ黒にはしない
AO=resize(ao,W,H)
Image.fromarray((np.clip(AO,0,1)*255).astype(np.uint8)).save('/tmp/ao.png')
np.save('/tmp/ao.npy',AO)
log('AO 最小%.2f 平均%.2f'%(AO.min(),AO.mean()))
