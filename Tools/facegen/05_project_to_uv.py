import numpy as np, time
from PIL import Image
from scipy import ndimage
T0=time.time()
def log(*a): print('[%5.1fs]'%(time.time()-T0), *a, flush=True)

W,H=2048,1024
OUT='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Textures/'
R_hi=np.load('/tmp/R_hi.npy')
alb=np.load('/tmp/alb_filled.npy'); valid=np.load('/tmp/valid.npy')
Wc=np.load('/tmp/tps_W.npy'); S=np.load('/tmp/tps_S.npy')
Hp,Wp,_=alb.shape

# 写真として信用できる度合い。輪郭で急に切れないよう、少しぼかす。
conf=ndimage.gaussian_filter(valid.astype(float),10)
conf=np.clip((conf-0.25)/0.50,0,1)

# 肌の色味だけを残した、なめらかな下地。
idx=ndimage.distance_transform_edt(~valid,return_distances=False,return_indices=True)
near=alb[tuple(idx)]
low=np.stack([ndimage.gaussian_filter(near[...,c],45) for c in range(3)],-1)
log('下地を作成')

# ---------- テクセルの立体位置 → 写真の座標 ----------
u=(np.arange(W)+0.5)/W; v=1-(np.arange(H)+0.5)/H
UU,VV=np.meshgrid(u,v)
th=(UU-0.5)*2*np.pi; ph=(VV-0.5)*np.pi
dx=np.cos(ph)*np.sin(th); dy=np.sin(ph); dz=np.cos(ph)*np.cos(th)
P=np.stack([dx,dy,dz],-1)*R_hi[...,None]
X=np.stack([P[...,0].ravel(),P[...,1].ravel()],1)
n=len(S); acc=np.zeros((len(X),2)); CH=400000
for i in range(0,len(X),CH):
    Xi=X[i:i+CH]
    d2=((Xi[:,None,:]-S[None,:,:])**2).sum(-1)
    K=np.where(d2>0, d2*0.5*np.log(np.maximum(d2,1e-12)), 0)
    acc[i:i+CH]=K@Wc[:n] + np.hstack([np.ones((len(Xi),1)),Xi])@Wc[n:]
PX=acc[:,0].reshape(H,W); PY=acc[:,1].reshape(H,W)
log('写真座標へ変換')

xs=np.clip(PX,0,Wp-1.001); ys=np.clip(PY,0,Hp-1.001)
x0=xs.astype(int); y0=ys.astype(int); fx=(xs-x0); fy=(ys-y0)
def samp(A):
    f3=A.ndim==3
    a=A[y0,x0]; b=A[y0,x0+1]; c=A[y0+1,x0]; d=A[y0+1,x0+1]
    if f3: g=fx[...,None]; h=fy[...,None]
    else:  g=fx; h=fy
    return a*(1-g)*(1-h)+b*g*(1-h)+c*(1-g)*h+d*g*h
photo=samp(alb); cf=samp(conf)
inside=((PX>2)&(PX<Wp-3)&(PY>2)&(PY<Hp-3)).astype(float)
log('取り出した')

wz=np.clip((dz-0.15)/0.35,0,1); wz=wz*wz*(3-2*wz)
w=wz*inside*cf

# 頭の後ろと側面。
# ここは写真に写っていない。薄板スプラインは対応点から離れると
# でたらめな所を指すので、外挿した色は使わず、
# 写真から測った肌の色を敷き、細かいむらだけ足す。
rng=np.random.default_rng(3)
def noise(a,b):
    g=(rng.random((b,a))*255).astype(np.uint8)
    return np.array(Image.fromarray(g).resize((W,H),Image.BICUBIC))/255.0
# 写真が届いている所の色を、テクスチャの上でゆっくり広げる。
# 写真の座標系で外挿すると破綻するが、テクスチャの上でならただの拡散で済む。
def blur_small(A,sig):
    a=np.array(Image.fromarray(A.astype(np.float32),mode='F').resize((W//4,H//4),Image.BILINEAR))
    a=ndimage.gaussian_filter(a,sig/4)
    return np.array(Image.fromarray(a,mode='F').resize((W,H),Image.BILINEAR)).astype(np.float64)
num=np.stack([blur_small(photo[...,c]*w,70) for c in range(3)],-1)
den=blur_small(w,70)[...,None]
tone_med=np.median(alb[valid].reshape(-1,3),axis=0)
spread=np.where(den>0.004, num/np.maximum(den,1e-6), tone_med[None,None,:])
k=np.clip(den/0.02,0,1)
tone=spread*k+tone_med[None,None,:]*(1-k)
mott=(noise(90,45)*0.6+noise(300,150)*0.4-0.5)*0.11
back=np.clip(tone*(1+mott)[...,None],0,1)
back=back*np.clip((VV-0.02)/0.14,0.55,1)[...,None]

col=np.clip(photo*w[...,None]+back*(1-w[...,None]),0,1)
Image.fromarray((col*255).astype(np.uint8)).save(OUT+'monden_face.png')
Image.fromarray((col*255).astype(np.uint8)).resize((1024,512)).save('/sessions/youthful-festive-cannon/mnt/outputs/albedo_uv.png')
log('アルベド 保存')
