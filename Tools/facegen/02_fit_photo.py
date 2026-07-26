# 正面写真をスキャン形状に投影して、アルベド（素の色）を作る。
#
# 対応づけの手がかりは眼鏡そのもの。写真の中の眼鏡も、スキャンから切り出した
# フレームも、同じ一本の眼鏡なので、レンズの穴の位置と大きさが共通の物差しになる。
# そこにマウス頼りの勘は要らない。穴を両方で自動検出し、その十点で写真を合わせる。
import numpy as np, time
from PIL import Image
from scipy import ndimage
T0=time.time()
def log(*a): print('[%5.1fs]'%(time.time()-T0), *a, flush=True)

W,H=2048,1024
PHOTO='/tmp/photo.png'
im=np.array(Image.open(PHOTO).convert('RGB')).astype(np.float64)/255.0
Hp,Wp,_=im.shape
log('写真', Wp, Hp)

# ---------- 写真の中のレンズ穴 ----------
g=im.mean(2)*255
roi=np.zeros_like(g,bool); roi[520:920, 80:960]=True
dark=(g<90)&roi
dark=ndimage.binary_closing(dark,structure=np.ones((5,5)))
lab,nl=ndimage.label(dark)
frame_ph=(lab==int(np.argmax(ndimage.sum(dark,lab,range(1,nl+1))))+1)
holes=ndimage.binary_fill_holes(frame_ph)&~frame_ph
hl,hn=ndimage.label(holes)
cand=sorted(((int((hl==i).sum()),i) for i in range(1,hn+1)),reverse=True)[:2]
ph=[]
for s,i in cand:
    ys,xs=np.nonzero(hl==i)
    ph.append(dict(cx=xs.mean(),cy=ys.mean(),x0=xs.min(),x1=xs.max(),y0=ys.min(),y1=ys.max()))
ph.sort(key=lambda e:e['cx'])
for e in ph: log('写真のレンズ穴 中心(%.0f,%.0f) x %d..%d y %d..%d'%(e['cx'],e['cy'],e['x0'],e['x1'],e['y0'],e['y1']))

# ---------- モデルのレンズ穴（正面から測った値）----------
md=[dict(cx=-0.135,cy=0.052,hx=0.093,hy=0.067),
    dict(cx= 0.134,cy=0.053,hx=0.093,hy=0.067)]

# ---------- 対応点 ----------
src=[]; dst=[]
for m,p in zip(md,ph):
    src += [(m['cx'],m['cy']), (m['cx']-m['hx'],m['cy']), (m['cx']+m['hx'],m['cy']),
            (m['cx'],m['cy']+m['hy']), (m['cx'],m['cy']-m['hy'])]
    dst += [(p['cx'],p['cy']), (p['x0'],p['cy']), (p['x1'],p['cy']),
            (p['cx'],p['y0']), (p['cx'],p['y1'])]
# 鼻先と口の合わせ目（写真から目視で読んだ値）
src += [(0.003,-0.074), (0.0,-0.233)]
dst += [(518.0, 935.0),  (528.0,1158.0)]
S=np.array(src); D=np.array(dst)

# ---------- アフィン当てはめ ----------
A=np.hstack([S,np.ones((len(S),1))])
M,_,_,_=np.linalg.lstsq(A,D,rcond=None)
pred=A@M
log('当てはめ残差 平均%.1fpx 最大%.1fpx'%(np.linalg.norm(pred-D,axis=1).mean(),np.linalg.norm(pred-D,axis=1).max()))
log('横の縮尺 %.0f px/単位, 縦 %.0f px/単位'%(abs(M[0,0]),abs(M[1,1])))

# ---------- 薄板スプラインで残差を吸収 ----------
def tps_fit(S,D,lam=1e-3):
    n=len(S)
    d2=((S[:,None,:]-S[None,:,:])**2).sum(-1)
    K=np.where(d2>0, d2*0.5*np.log(np.maximum(d2,1e-12)), 0)
    P=np.hstack([np.ones((n,1)),S])
    L=np.zeros((n+3,n+3))
    L[:n,:n]=K+lam*np.eye(n); L[:n,n:]=P; L[n:,:n]=P.T
    Y=np.vstack([D,np.zeros((3,2))])
    return np.linalg.solve(L,Y)
def tps_eval(Wc,S,X):
    n=len(S)
    d2=((X[:,None,:]-S[None,:,:])**2).sum(-1)
    K=np.where(d2>0, d2*0.5*np.log(np.maximum(d2,1e-12)), 0)
    return K@Wc[:n] + np.hstack([np.ones((len(X),1)),X])@Wc[n:]
Wc=tps_fit(S,D)
log('スプライン残差 %.2fpx'%np.linalg.norm(tps_eval(Wc,S,S)-D,axis=1).max())

# ---------- 肌だけの領域 ----------
R,G,B=im[...,0],im[...,1],im[...,2]
Y=im.mean(2)
skin=(R>G+0.02)&(G>B-0.02)&(Y>0.25)&(Y<0.96)
skin&=~ndimage.binary_dilation(frame_ph,iterations=6)
skin=ndimage.binary_opening(skin,structure=np.ones((7,7)))
skin=ndimage.binary_closing(skin,structure=np.ones((15,15)))
sl,sn=ndimage.label(skin)
skin=(sl==int(np.argmax(ndimage.sum(skin,sl,range(1,sn+1))))+1)
skin=ndimage.binary_fill_holes(skin)
log('肌の画素 %d (%.1f%%)'%(skin.sum(),100*skin.sum()/skin.size))
Image.fromarray((skin*255).astype(np.uint8)).save('/tmp/skinmask.png')

# ---------- 陰影を落として、素の色にする ----------
# 写真には撮影時の光が焼き付いている。そのまま貼ると、Unity の光と二重になる。
# ゆっくりした明暗（＝照明）を割り、細かい明暗（＝毛穴やしみ）だけ残す。
lum=np.where(skin,Y,np.nan)
big=np.array(Image.fromarray((np.nan_to_num(lum,nan=np.nanmedian(lum))*255).astype(np.uint8))
             .filter(__import__('PIL.ImageFilter',fromlist=['x']).GaussianBlur(90)))/255.0
albedo=np.clip(im/np.maximum(big,0.05)[...,None]*np.nanmedian(lum),0,1)
albedo=albedo*0.72+im*0.28      # 完全に平らにすると生気が抜けるので少し残す
Image.fromarray((albedo*255).astype(np.uint8)).save('/tmp/albedo_photo.png')

np.save('/tmp/tps_W.npy',Wc); np.save('/tmp/tps_S.npy',S)
np.save('/tmp/albedo.npy',albedo); np.save('/tmp/skin.npy',skin)
log('保存完了')
