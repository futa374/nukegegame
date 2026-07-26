# 高解像度スキャンから、間引き版に貼るための質感マップを焼く。
#
# 球面UVなので、頭の表面は「向き(u,v) → 中心からの距離 r」という
# 高さの地形として扱える。これを使えば、レイトレーサを書かなくても
# 凹凸（法線）と、くぼみの暗さ（AO）を素直に求められる。
import numpy as np, struct, time
from PIL import Image
from scipy.interpolate import LinearNDInterpolator

W,H=2048,1024
T0=time.time()
def log(*a): print('[%5.1fs]'%(time.time()-T0), *a, flush=True)

# ---------- 高解像度スキャン ----------
src='/sessions/youthful-festive-cannon/mnt/uploads/monden_head_noneck.stl'
buf=open(src,'rb').read(); n=struct.unpack('<I',buf[80:84])[0]
dd=np.frombuffer(buf,dtype=np.uint8,count=n*50,offset=84).reshape(n,50)
tris=dd[:,12:48].copy().view(np.float32).reshape(n,3,3).astype(np.float64)
V0=tris.reshape(-1,3)
uq,inv=np.unique(np.round(V0,3),axis=0,return_inverse=True)
V=np.zeros_like(uq); c=np.zeros(len(uq)); np.add.at(V,inv,V0); np.add.at(c,inv,1); V/=c[:,None]
HI=np.stack([V[:,0],V[:,2],-V[:,1]],1)
HI-=(HI.max(0)+HI.min(0))/2; HI/=(HI.max(0)-HI.min(0)).max()
log('高解像度', HI.shape)

# ---------- 間引き版（Unity に入っているモデル）----------
LO=np.array([[float(t) for t in L.split()[1:4]]
             for L in open('/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Models/monden_head.obj')
             if L.startswith('v ')])
log('間引き版', LO.shape)

def radius_map(P):
    """点群を球面UVの地形図（中心からの距離）に変換する。"""
    r=np.linalg.norm(P,axis=1); d=P/r[:,None]
    u=(np.arctan2(d[:,0],d[:,2])/(2*np.pi))+0.5
    v=np.arcsin(np.clip(d[:,1],-1,1))/np.pi+0.5
    # 継ぎ目（u=0 と u=1）でちぎれないよう、左右に写しを足す
    uu=np.concatenate([u,u-1,u+1]); vv=np.concatenate([v,v,v]); rr=np.concatenate([r,r,r])
    keep=(uu>-0.15)&(uu<1.15)
    f=LinearNDInterpolator(np.stack([uu[keep],vv[keep]],1), rr[keep])
    gu=(np.arange(W)+0.5)/W; gv=1-(np.arange(H)+0.5)/H
    GU,GV=np.meshgrid(gu,gv)
    R=f(GU,GV)
    # 極（頭頂・首の切り口）は三角形が無く欠けるので、上下から埋める
    bad=~np.isfinite(R)
    if bad.any():
        for row in range(H):
            if bad[row].all(): continue
            break
        R[bad]=np.nan
        # 行ごとに、近い有効行の値で埋める
        valid_rows=np.where(~np.isnan(R).all(1))[0]
        for row in np.where(np.isnan(R).any(1))[0]:
            nearest=valid_rows[np.argmin(np.abs(valid_rows-row))]
            m=np.isnan(R[row])
            R[row][m]=np.nanmean(R[nearest]) if np.isnan(R[nearest]).all() else np.interp(
                np.flatnonzero(m), np.flatnonzero(~np.isnan(R[nearest])), R[nearest][~np.isnan(R[nearest])])
    return R

R_hi=radius_map(HI); log('高解像度の地形図', np.nanmin(R_hi), np.nanmax(R_hi))
R_lo=radius_map(LO); log('間引き版の地形図')
np.save('/tmp/R_hi.npy',R_hi); np.save('/tmp/R_lo.npy',R_lo)
log('保存完了')
