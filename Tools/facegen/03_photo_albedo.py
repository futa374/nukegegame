import numpy as np, time
from PIL import Image, ImageFilter, ImageDraw
from scipy import ndimage
T0=time.time()
def log(*a): print('[%5.1fs]'%(time.time()-T0), *a, flush=True)

im=np.array(Image.open('/tmp/photo.png').convert('RGB')).astype(np.float64)/255.0
Hp,Wp,_=im.shape
Q=np.load('/tmp/Q.npy')
Wc=np.load('/tmp/tps_W.npy'); S=np.load('/tmp/tps_S.npy')
def tps(X):
    n=len(S)
    d2=((X[:,None,:]-S[None,:,:])**2).sum(-1)
    K=np.where(d2>0, d2*0.5*np.log(np.maximum(d2,1e-12)), 0)
    return K@Wc[:n] + np.hstack([np.ones((len(X),1)),X])@Wc[n:]

# 眼鏡（写真側）
g=im.mean(2)*255
roi=np.zeros_like(g,bool); roi[520:920, 80:960]=True
dark=ndimage.binary_closing((g<90)&roi,structure=np.ones((5,5)))
lab,nl=ndimage.label(dark)
blob=(lab==int(np.argmax(ndimage.sum(dark,lab,range(1,nl+1))))+1)

# 顔の範囲：モデルの輪郭を写真へ写し、少し内側に取る（壁と髪を拾わないため）
front=Q[Q[:,2]>-0.05][:,:2]
ang=np.arctan2(front[:,1],front[:,0]); rad=np.linalg.norm(front,axis=1)
NB=180; edge=[]
bi=np.clip(((ang+np.pi)/(2*np.pi)*NB).astype(int),0,NB-1)
for b in range(NB):
    m=bi==b
    if m.any(): edge.append(front[m][np.argmax(rad[m])])
edge=np.array(edge)
poly=tps(edge*0.82)
face_region=Image.new('L',(Wp,Hp),0)
ImageDraw.Draw(face_region).polygon([tuple(p) for p in poly],fill=255)
face_region=np.array(face_region)>128

# 目の窓：レンズの穴の中央あたりを、色に関わらず使う
eyes=Image.new('L',(Wp,Hp),0); ed=ImageDraw.Draw(eyes)
for cx,cy,hw,hh in ((321,746,145,118),(741,750,138,116)):
    ed.ellipse([cx-hw*0.64,cy-hh*0.56,cx+hw*0.64,cy+hh*0.56],fill=255)
eyes=(np.array(eyes)>128)&~ndimage.binary_dilation(blob&~ndimage.binary_fill_holes(blob),iterations=2)
# レンズに映り込んだ壁の白は、目ではない
eyes&=(im.mean(2)<0.80)

R,G,B=im[...,0],im[...,1],im[...,2]
Y=im.mean(2)
skin=(R>G+0.025)&(G>B-0.01)&(Y>0.30)&(Y<0.95)&face_region
skin=ndimage.binary_opening(skin,structure=np.ones((7,7)))
sl,sn=ndimage.label(skin)
if sn: skin=(sl==int(np.argmax(ndimage.sum(skin,sl,range(1,sn+1))))+1)
skin&=~ndimage.binary_dilation(blob,iterations=4)
valid=skin|eyes
log('肌 %d / 目 %d / 合計 %d'%(skin.sum(),eyes.sum(),valid.sum()))

# 陰影を落とす（撮影時の光を Unity の光と二重にしない）
med=np.median(Y[skin])
low=np.array(Image.fromarray((np.where(skin,Y,med)*255).astype(np.uint8))
             .filter(ImageFilter.GaussianBlur(80)))/255.0
alb=np.clip(im/np.maximum(low,0.05)[...,None]*med,0,1)
alb=alb*0.75+im*0.25

# 皮脂の照り返しを落とす。
# 写真に写った白い照りは色ではなく光なので、アルベドに残すと
# どの向きから照らしても鼻筋が白いままの、蝋のような顔になる。
# まわりより明るすぎる分だけを、色味を保ったまま押し戻す。
Ya=alb.mean(2)
base=np.array(Image.fromarray((Ya*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(14)))/255.0
over=Ya-(base+0.055)
hot=(over>0)&skin
scale=np.ones_like(Ya)
scale[hot]=((base+0.055)[hot]+ (over[hot]*0.25))/np.maximum(Ya[hot],1e-6)
alb=np.clip(alb*scale[...,None],0,1)
print('照りを抑えた画素 %d'%hot.sum())

# 彩度。陰影を割ると色が抜けるので、肌の赤みを戻す。
g=alb.mean(2,keepdims=True)
alb=np.clip(g+(alb-g)*1.30,0,1)

# 使えない所を、肌だけを種にして広げる
a=alb.copy(); a[~valid]=0; v=valid.copy()
for it in range(80):
    if v.all(): break
    den=ndimage.uniform_filter(v.astype(float),11)
    num=np.stack([ndimage.uniform_filter(a[...,c]*v,11) for c in range(3)],-1)
    grow=(den>0.03)&~v
    a[grow]=(num/np.maximum(den,1e-6)[...,None])[grow]
    v|=grow
a=np.stack([ndimage.gaussian_filter(a[...,c],1.2) for c in range(3)],-1)
a[valid]=alb[valid]
Image.fromarray((a*255).astype(np.uint8)).save('/sessions/youthful-festive-cannon/mnt/outputs/albedo_photo.png')
np.save('/tmp/alb_filled.npy',a); np.save('/tmp/valid.npy',valid)
log('完了')
