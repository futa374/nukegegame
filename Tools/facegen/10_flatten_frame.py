import numpy as np
from scipy import ndimage
M='/sessions/youthful-festive-cannon/mnt/Documents/nukegegame/Assets/Models/'
W,H=512,256
R=np.load('/tmp/R_hi.npy')[::4,::4].astype(np.float32).copy()
gv=np.array([[float(t) for t in L.split()[1:4]] for L in open(M+'monden_glasses.obj') if L.startswith('v ')],dtype=np.float32)
d=gv/np.linalg.norm(gv,axis=1,keepdims=True)
gu=(((np.arctan2(d[:,0],d[:,2])/(2*np.pi))+0.5)*W).astype(int)
gy=((1-(np.arcsin(np.clip(d[:,1],-1,1))/np.pi+0.5))*H).astype(int)
gm=np.zeros((H,W),bool); gm[np.clip(gy,0,H-1),np.clip(gu,0,W-1)]=True
soft=np.clip(ndimage.gaussian_filter(ndimage.binary_dilation(gm,iterations=3).astype(np.float32),2.0)*3.0,0,1)
out=(soft<0.02).astype(np.float32)
num=ndimage.gaussian_filter(R*out,4.0); den=ndimage.gaussian_filter(out,4.0)
Rskin=np.where(den>1e-4,num/np.maximum(den,1e-6),R)
Rt=R*(1-soft)+Rskin*soft
del num,den,Rskin,R

res=[]; moved=0
for L in open(M+'monden_head.obj'):
    L=L.rstrip('\n')
    if not L.startswith('v '): res.append(L); continue
    x,y,z=[float(t) for t in L.split()[1:4]]
    r=(x*x+y*y+z*z)**0.5
    dx,dy,dz=x/r,y/r,z/r
    ix=int(min(max(((np.arctan2(dx,dz)/(2*np.pi))+0.5)*W,0),W-1))
    iy=int(min(max((1-(np.arcsin(max(-1,min(1,dy)))/np.pi+0.5))*H,0),H-1))
    s=float(soft[iy,ix])
    if s>0.01:
        nr=r*(1-s)+float(Rt[iy,ix])*s
        x,y,z=dx*nr,dy*nr,dz*nr; moved+=1
    res.append('v %.6f %.6f %.6f'%(x,y,z))
open(M+'monden_head.obj','w').write('\n'.join(res)+'\n')
print('ならした頂点 %d'%moved)
