import numpy as np
from PIL import Image
from scipy import ndimage

W,H=2048,1024
OUT='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Textures/'
R_hi=np.load('/tmp/R_hi.npy'); R_lo=np.load('/tmp/R_lo.npy')
v=1-(np.arange(H)+0.5)/H
cosphi=np.maximum(np.cos((v-0.5)*np.pi),0.15)[:,None]
def slopes(R):
    return (np.gradient(R,axis=1)*W)/(2*np.pi*R*cosphi), (-np.gradient(R,axis=0)*H)/(np.pi*R)

# 眼鏡は別メッシュなので、肌の凹凸からは外す
gv=np.array([[float(t) for t in L.split()[1:4]] for L in open(OUT+'../Models/monden_glasses.obj') if L.startswith('v ')])
d=gv/np.linalg.norm(gv,axis=1,keepdims=True)
gu=((np.arctan2(d[:,0],d[:,2])/(2*np.pi))+0.5)*W
gy=(1-(np.arcsin(np.clip(d[:,1],-1,1))/np.pi+0.5))*H
gm=np.zeros((H,W),bool)
gm[np.clip(gy.astype(int),0,H-1),np.clip(gu.astype(int),0,W-1)]=True
soft=np.clip(ndimage.gaussian_filter(ndimage.binary_dilation(gm,iterations=11).astype(float),9)*3.0,0,1)

def deglass(R):
    return R*(1-soft)+ndimage.gaussian_filter(R,14)*soft
Rh=deglass(R_hi); Rl=deglass(R_lo)

# ここが肝。間引いたメッシュの「面」の傾きと比べてはいけない。
# Unity は頂点法線を滑らかに補間して陰影を付けるので、
# 比べる相手は三角形の平面ではなく、その滑らかな面でなければならない。
# 面と比べると、三角形の切り替わりが段差として法線マップに焼き付き、
# 顔じゅうが割れたような質感になる。
tri_px=9.0        # 13,000頂点だと三角形はおよそこの大きさ
Rl_smooth=ndimage.gaussian_filter(Rl,tri_px)
Rh_s=ndimage.gaussian_filter(Rh,1.0)

gx_hi,gy_hi=slopes(Rh_s); gx_lo,gy_lo=slopes(Rl_smooth)
nx=-(gx_hi-gx_lo); ny=-(gy_hi-gy_lo)
m=np.hypot(nx,ny); lim=0.8
sc=np.where(m>lim, lim/np.maximum(m,1e-6), 1.0); nx*=sc; ny*=sc

rng=np.random.default_rng(7)
def fine(f):
    g=rng.normal(0,1,(H//f,W//f))
    g=np.array(Image.fromarray(((g-g.min())/np.ptp(g)*255).astype(np.uint8)).resize((W,H),Image.BICUBIC))/255.0-0.5
    return g
pore=fine(2)*0.55+fine(4)*0.30+fine(8)*0.15
uu=(np.arange(W)+0.5)/W; UU,VV=np.meshgrid(uu,v)
amt=np.clip((np.cos((UU-0.5)*2*np.pi)-0.2)/0.8,0,1)*np.clip(1-np.abs(VV-0.55)/0.35,0,1)
py_,px_=np.gradient(pore*amt)
nx+= px_*W*0.00035; ny+=-py_*H*0.00035

L=np.sqrt(nx**2+ny**2+1)
Image.fromarray(((np.stack([nx/L,ny/L,np.ones_like(nx)/L],-1)*0.5+0.5)*255).astype(np.uint8)).save(OUT+'monden_face_normal.png')
print('法線 傾き 平均%.3f 最大%.3f'%(np.hypot(nx,ny).mean(),np.hypot(nx,ny).max()))
Image.open(OUT+'monden_face_normal.png').crop((780,300,1280,760)).save('/sessions/youthful-festive-cannon/mnt/outputs/chk_nrm.png')

# アルベドを、シーンの明るさに合わせて持ち上げる
alb=np.array(Image.open(OUT+'monden_face.png')).astype(float)/255.0
cur=alb[380:760,820:1240].mean()
alb=np.clip(alb*(0.700/cur),0,1)
Image.fromarray((alb*255).astype(np.uint8)).save(OUT+'monden_face.png')
print('アルベド %.3f → %.3f'%(cur,alb[380:760,820:1240].mean()))
