# 顔テクスチャ（肌・目・眉・唇）。眼鏡はここでは描かない。
#
# ■ 眼鏡を描かない理由
# UV が球面投影なので、フレームとその裏の肌が同じ座標を共有する。
# 塗りを広げるほど肌に黒がはみ出す。フレームは別メッシュ
# （monden_glasses.obj）として切り出し、黒いマテリアルで頭にかぶせている。
#
# ■ 目の位置を決める考え方
# レンズの穴の「向き」に目を置くと、必ず内側かつ下にずれる。
# 球面投影の UV は頭の中心から見た向きで決まるのに対し、
# 目はレンズより手前ではなく奥（＝中心に近い側）の肌にあるからだ。
# 中心から見て同じ向きにある肌の点は、レンズそのものより
# 中心軸に寄った位置——つまり内側・下——に来てしまう。
#
# 正面から見て重なる位置に描くには、向きではなく (x, y) を合わせる。
# レンズの穴を正面（xy平面）で測り、その位置で顔のいちばん手前にある
# 肌の点を拾い、その点の UV に目を描く。
import numpy as np, struct
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

W,H=2048,1024
OUT='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Textures/monden_face.png'
GLASSES='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Models/monden_glasses.obj'

# ---------- 高解像度スキャンを、モデルと同じ座標系へ ----------
src='/sessions/youthful-festive-cannon/mnt/uploads/monden_head_noneck.stl'
buf=open(src,'rb').read(); n=struct.unpack('<I',buf[80:84])[0]
dd=np.frombuffer(buf,dtype=np.uint8,count=n*50,offset=84).reshape(n,50)
tris=dd[:,12:48].copy().view(np.float32).reshape(n,3,3).astype(np.float64)
V0=tris.reshape(-1,3)
uq,inv=np.unique(np.round(V0,3),axis=0,return_inverse=True)
V=np.zeros_like(uq); c=np.zeros(len(uq)); np.add.at(V,inv,V0); np.add.at(c,inv,1); V/=c[:,None]
Q=np.stack([V[:,0],V[:,2],-V[:,1]],1)
Q-=(Q.max(0)+Q.min(0))/2; Q/=(Q.max(0)-Q.min(0)).max()
FACE=Q[Q[:,2]>0.0]

def px_of(p):
    d=p/np.linalg.norm(p)
    u=(np.arctan2(d[0],d[2])/(2*np.pi))+0.5
    v=np.arcsin(np.clip(d[1],-1,1))/np.pi+0.5
    return np.array([u*W,(1-v)*H])

def face_px(x,y,rad=0.012):
    """正面から見て (x, y) の位置にある顔の肌が、テクスチャのどこに来るか。

    窓のなかで単純に最前面の点を採ると、顔は外へ向かって奥へ逃げるので、
    いつも窓の内側寄りの点が選ばれ、測るほど中央へ縮んでしまう。
    そこで、まず最前面の「層」を取り出し、そのなかで指定した (x, y) に
    いちばん近い点を選ぶ。"""
    m=(np.abs(FACE[:,0]-x)<rad)&(np.abs(FACE[:,1]-y)<rad)
    while m.sum()<8 and rad<0.06:
        rad*=1.5; m=(np.abs(FACE[:,0]-x)<rad)&(np.abs(FACE[:,1]-y)<rad)
    sub=FACE[m]
    sub=sub[sub[:,2] > sub[:,2].max()-0.025]          # 最前面の層だけ
    k=np.argmin((sub[:,0]-x)**2+(sub[:,1]-y)**2)      # そのなかで狙った位置に最も近い点
    return px_of(sub[k])

# ---------- レンズの穴を正面から測る ----------
gv=np.array([[float(t) for t in L.split()[1:4]] for L in open(GLASSES) if L.startswith('v ')])
rim=gv[gv[:,2]>0.28]
N=400
lo=rim[:,:2].min(0)-0.01; hi=rim[:,:2].max(0)+0.01
grid=np.zeros((N,N),bool)
ii=((rim[:,:2]-lo)/(hi-lo)*(N-1)).astype(int)
grid[ii[:,1],ii[:,0]]=True
grid=ndimage.binary_closing(ndimage.binary_dilation(grid,iterations=2),structure=np.ones((5,5)))
lab,nl=ndimage.label(~grid)
border=set(lab[0,:].tolist())|set(lab[-1,:].tolist())|set(lab[:,0].tolist())|set(lab[:,-1].tolist())
holes=sorted(((int((lab==k).sum()),k) for k in range(1,nl+1) if k not in border),reverse=True)[:2]
lens=[]
for sz,k in holes:
    ys,xs=np.nonzero(lab==k)
    f=lambda a,i: lo[i]+a/(N-1)*(hi[i]-lo[i])
    lens.append(dict(cx=f(xs.mean(),0), cy=f(ys.mean(),1),
                     hx=(f(xs.max(),0)-f(xs.min(),0))/2, hy=(f(ys.max(),1)-f(ys.min(),1))/2))
lens.sort(key=lambda e:e['cx'])
y_frame_top=rim[:,1].max()
for e,nm in zip(lens,('画面左','画面右')):
    print('%s レンズ穴 中心(%.3f,%.3f) 半幅%.3f 半高%.3f' % (nm,e['cx'],e['cy'],e['hx'],e['hy']))
print('フレーム上端 y=%.3f' % y_frame_top)

# ---------- 肌 ----------
rng=np.random.default_rng(3)
def noise(fx,fy):
    g=(rng.random((fy,fx))*255).astype(np.uint8)
    return np.array(Image.fromarray(g).resize((W,H),Image.BICUBIC))/255.0
yy,xx=np.mgrid[0:H,0:W]; uu=xx/W; vv=1-yy/H
base=np.array([0.86,0.68,0.57]); shadow=np.array([0.60,0.43,0.35]); flush=np.array([0.85,0.57,0.49])
mott=noise(70,35)*0.6+noise(260,130)*0.4
col=shadow+(base-shadow)*(0.55+0.45*mott)[...,None]
fw=np.exp(-((uu-0.5)/0.18)**2)*np.exp(-((vv-0.50)/0.26)**2)
col=col*(1-0.28*fw[...,None])+flush*(0.28*fw[...,None])
col=shadow+(col-shadow)*np.clip((vv-0.02)/0.16,0,1)[...,None]
img=Image.fromarray((np.clip(col,0,1)*255).astype(np.uint8))
dr=ImageDraw.Draw(img,'RGBA')
def ell(c,ax,ay,fill): dr.ellipse([c[0]-ax,c[1]-ay,c[0]+ax,c[1]+ay],fill=fill)

# ---------- 目 ----------
for i,e in enumerate(lens):
    inward = +1 if i==0 else -1
    ctr = face_px(e['cx'], e['cy'])
    xl  = face_px(e['cx']-e['hx']*0.80, e['cy'])
    xr  = face_px(e['cx']+e['hx']*0.80, e['cy'])
    yt  = face_px(e['cx'], e['cy']+e['hy']*0.80)
    yb  = face_px(e['cx'], e['cy']-e['hy']*0.80)
    hw = abs(xr[0]-xl[0])/2 / 0.80        # レンズ穴の半幅（テクセル）
    hh = abs(yb[1]-yt[1])/2 / 0.80
    cx0 = (xl[0]+xr[0])/2
    cy0 = (yt[1]+yb[1])/2
    print('%s 目の中心 px=(%.0f,%.0f) 穴 半幅%.0f 半高%.0f' % ('LR'[i],cx0,cy0,hw,hh))

    ex = cx0 + inward*hw*0.05
    ey = cy0 - hh*0.02
    ew = hw*0.70; eh = hh*0.32
    ell((ex,ey), ew, eh, (244,240,234,255))            # 白目
    ell((ex,ey), eh*0.90, eh*0.90, (72,48,32,255))     # 虹彩
    ell((ex,ey), eh*0.38, eh*0.38, (16,13,11,255))     # 瞳孔
    ell((ex,ey-eh*0.95), ew, eh*0.28, (46,34,27,255))  # 上まぶた

    # ---------- 眉 ----------
    # フレームの上端より少し上の肌に置く。ここも向きではなく (x, y) で合わせる。
    for t in np.linspace(0,1,26):
        s = -inward
        bx = e['cx'] + s*(t-0.45)*e['hx']*1.75
        by = y_frame_top + 0.016 + 0.008*np.sin(t*np.pi) - 0.009*t*t
        p = face_px(bx,by,0.010)
        ell(p, hw*0.13*(1.1-0.45*t), 6.0*(1.2-0.6*t), (42,31,25,255))

# ---------- 唇 ----------
# 顔の正中線の断面で、鼻先 → 鼻の下のくぼみ → 上唇 → 合わせ目 → 下唇 → 顎
# という起伏を追い、いちばん深いくぼみを口の合わせ目とする。
mid=Q[(np.abs(Q[:,0])<0.008)&(Q[:,2]>0.0)]
y_nose=Q[np.argmax(Q[:,2])][1]
y_chin=Q[Q[:,2]>0.1][:,1].min()
span=y_nose-y_chin
prof=[]
for y_ in np.arange(y_nose-0.50*span, y_nose-0.14*span, 0.006):
    sl=mid[np.abs(mid[:,1]-y_)<0.006]
    if len(sl): prof.append((y_, sl[:,2].max()))
prof=np.array(prof)
dips=[i for i in range(1,len(prof)-1) if prof[i,1]<prof[i-1,1] and prof[i,1]<prof[i+1,1]]
peaks=[i for i in range(1,len(prof)-1) if prof[i,1]>prof[i-1,1] and prof[i,1]>prof[i+1,1]]
mouth_y = prof[min(dips, key=lambda i: prof[i,1]),0] if dips else y_nose-0.15
upper_y = prof[max([i for i in peaks if prof[i,0]>mouth_y], default=0),0] if peaks else mouth_y+0.04
lower_y = prof[max([i for i in peaks if prof[i,0]<mouth_y], default=0),0] if peaks else mouth_y-0.04
print('断面から: 合わせ目 y=%.3f / 上唇 y=%.3f / 下唇 y=%.3f' % (mouth_y,upper_y,lower_y))

m=face_px(0.0,mouth_y); up=face_px(0.0,upper_y); lw=face_px(0.0,lower_y)
hwp=abs(face_px(0.062,mouth_y)[0]-m[0])
ell(((up[0]+m[0])/2,(up[1]+m[1])/2), hwp, abs(up[1]-m[1])*0.62, (163,92,85,255))
ell(((lw[0]+m[0])/2,(lw[1]+m[1])/2), hwp*0.94, abs(lw[1]-m[1])*0.72, (183,108,99,255))
ell(m, hwp*0.97, 1.6, (112,58,55,255))
print('口 px=(%.0f,%.0f) 横半径%.0f' % (m[0],m[1],hwp))

img=img.filter(ImageFilter.GaussianBlur(1.4))
img.save(OUT)
print('保存:',OUT)
