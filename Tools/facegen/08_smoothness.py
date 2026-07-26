# 粗さ（スムースネス）の分布。
# 一様な光沢は、肌ではなくプラスチックに見える。額と鼻筋は皮脂で照り、
# 頬と目のまわりは乾いてマットになる。その差だけを一枚に描く。
import numpy as np
from PIL import Image
from scipy import ndimage
W,H=2048,1024
OUT='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Textures/'
u=(np.arange(W)+0.5)/W; v=1-(np.arange(H)+0.5)/H
UU,VV=np.meshgrid(u,v)
rng=np.random.default_rng(11)
def noise(a,b):
    g=(rng.random((b,a))*255).astype(np.uint8)
    return np.array(Image.fromarray(g).resize((W,H),Image.BICUBIC))/255.0

sm=np.full((H,W),0.15)
for cu,cv,ru,rv,val in [(0.500,0.620,0.100,0.090,0.26),   # 額
                        (0.500,0.505,0.030,0.090,0.28),   # 鼻筋
                        (0.500,0.445,0.045,0.028,0.27),   # 鼻先
                        (0.500,0.300,0.048,0.038,0.24),   # 顎
                        (0.500,0.337,0.048,0.016,0.40),   # 唇
                        (0.455,0.470,0.045,0.050,0.12),   # 頬
                        (0.545,0.470,0.045,0.050,0.12)]:
    w=np.exp(-(((UU-cu)/ru)**2+((VV-cv)/rv)**2)*2.2)
    sm=sm*(1-w)+val*w
sm=ndimage.gaussian_filter(sm,10)

# 目だけは別。角膜は濡れていて、まわりの肌とは比べものにならないほど強く光を返す。
# 写真から取った目がいちばん嘘っぽく見えるのは、この照り返しが無いからで、
# ここに一点の光が入るかどうかで、生きている目かどうかが決まる。
for cu,cv,ru,rv in [(0.4302,0.5449,0.0360,0.0195),(0.5684,0.5459,0.0360,0.0195)]:
    w=np.exp(-(((UU-cu)/ru)**2+((VV-cv)/rv)**2)*2.6)
    sm=sm*(1-w)+0.86*w
sm=ndimage.gaussian_filter(sm,2)
sm+=(noise(340,170)-0.5)*0.035
sm=np.clip(sm,0.05,0.90)
m=np.zeros((H,W,4),np.uint8)
m[...,3]=(sm*255).astype(np.uint8)
Image.fromarray(m,'RGBA').save(OUT+'monden_face_mask.png')
print('スムースネス 平均%.2f 最大%.2f'%(sm.mean(),sm.max()))
