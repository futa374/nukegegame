import numpy as np
from PIL import Image
from scipy import ndimage
p='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Textures/monden_face.png'
a=np.array(Image.open(p).convert('RGB')).astype(np.float32)/255
H,W,_=a.shape
uu,vv=np.meshgrid((np.arange(W)+0.5)/W, 1-(np.arange(H)+0.5)/H)
keep=np.zeros((H,W),bool)
for cu,cv,ru,rv in [(0.4302,0.5449,0.042,0.024),(0.5684,0.5459,0.042,0.024)]:
    keep |= (((uu-cu)/ru)**2+((vv-cv)/rv)**2) < 1.0
for it in range(3):
    y=a.mean(2); lo=ndimage.uniform_filter(y,17)
    hot=((y-lo)>0.055)&~keep
    hot=ndimage.binary_dilation(hot,iterations=2)
    med=np.stack([ndimage.median_filter(a[...,c],13) for c in range(3)],-1)
    s=np.clip(ndimage.gaussian_filter(hot.astype(np.float32),2.0)*2.2,0,1)[...,None]
    a=a*(1-s)+med*s
    print('回%d: %d画素'%(it+1,hot.sum()))
Image.fromarray((np.clip(a,0,1)*255).astype(np.uint8)).save(p)
r=a.mean(2)[300:760,780:1280]; d=r-ndimage.uniform_filter(r,15)
print('残り >0.10: %d'%int((d>0.10).sum()))
