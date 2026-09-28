"""Audit shipped VAT envelopes and planted-foot travel; requires numpy and source GLB.
Run: python Tools/AuditGoblinLocomotion.py --output Logs/CrowdEnvelope
The source GLB is read only and is used only to label mesh vertices by bones.
"""
import argparse, json, pathlib, struct
import numpy as np
from BakeGoblinPreview import load_glb

def atlas(path):
    data = path.read_bytes()
    _, vertices, indices, frames, width, height, duration = struct.unpack_from('<6If', data)
    positions = np.frombuffer(data, '<f2', count=width*height*4,
        offset=28+vertices*32+indices*4).reshape(-1,4)[:vertices*frames,:3]
    return positions.astype(float).reshape(frames,vertices,3), duration

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--output', type=pathlib.Path, default=pathlib.Path('Logs/CrowdEnvelope'))
    args=parser.parse_args(); args.output.mkdir(parents=True, exist_ok=True)
    root=pathlib.Path(__file__).resolve().parents[1]/'Assets/Resources/GoblinPreview/Red'
    source=pathlib.Path(json.loads((root/'Running.report.json').read_text())['source'])
    gltf, _, accessor=load_glb(source)
    mesh=next(n for n in gltf['nodes'] if 'mesh' in n and 'skin' in n)
    attributes=gltf['meshes'][mesh['mesh']]['primitives'][0]['attributes']
    joints=accessor(attributes['JOINTS_0']).astype(int); weights=accessor(attributes['WEIGHTS_0'])
    names=np.array([gltf['nodes'][i]['name'] for i in gltf['skins'][mesh['skin']]['joints']])
    labels=names[joints[np.arange(len(joints)),weights.argmax(1)]]
    result={'method':'Dominant skin bone labels; all shipped half-float VAT vertices, all frames. Radii before 1.04 packing skin.', 'clips':{}}
    for name in ['Running','RunFast','Run02','Run03','ChargedSlash','ChargedUpwardSlash']:
        p,duration=atlas(root/(name+'.bytes'))
        entry={'frames':len(p),'duration':duration,'radius':float(np.linalg.norm(p[:,:,[0,2]],axis=-1).max())}
        for part, words in {'torso':['Spine','Hips'],'shoulders_upper_arms':['Shoulder','Arm']}.items():
            mask=np.array([any(word in label for word in words) for label in labels])
            entry[part+'_radius']=float(np.linalg.norm(p[:,mask,:][:,:,[0,2]],axis=-1).max())
        strides=[]
        if name in ['Running','RunFast','Run02','Run03']:
            for side in ['LeftFoot','RightFoot']:
                foot=p[:,labels==side,:].mean(axis=1)
                backward=-(np.roll(foot[:,2],-1)-foot[:,2])*len(p)
                stance=(foot[:,1]<np.quantile(foot[:,1],.45)) & (backward>0)
                strides.extend(backward[stance].tolist())
            old={'Running':8.8,'RunFast':8.2,'Run02':9.2,'Run03':9.0}[name]
            entry['stance_stride_median']=float(np.median(strides))
            entry['old_median_absolute_slip_fraction']=float(np.median(np.abs(1-np.array(strides)/old)))
            entry['calibrated_median_absolute_slip_fraction']=float(np.median(np.abs(1-np.array(strides)/3.73)))
        result['clips'][name]=entry
    brace,_=atlas(root/'ChargedUpwardSlash.bytes'); pose=brace[71].copy()
    # Max horizontal perturbation from the two breathing sinusoids; conservative bound.
    brace_radius=float(np.linalg.norm(pose[:,[0,2]],axis=-1).max()+np.sqrt(2)*.025)
    result['locomotion']={'running_clip':'Running','stride':3.73,'brace_phase':71/97,
        'brace_radius_with_breathing_bound':brace_radius,'protected_radius':.96,
        'packing_skin':1.04,'world_stride_at_scale_022':3.73*.22*1.04,
        'scope':'Locomotion and brace. Combat slashes have a larger action envelope.'}
    assert result['clips']['Running']['radius'] <= .96
    assert brace_radius <= .96
    (args.output/'vat-audit.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result,indent=2))
if __name__=='__main__': main()
