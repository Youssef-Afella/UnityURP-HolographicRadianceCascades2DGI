using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable, VolumeComponentMenuForRenderPipeline("HRC2DGI/Holographic Radiance Cascades 2D GI", typeof(UniversalRenderPipeline))]
public class HolographicRadianceCascades2DGIVolume : VolumeComponent
{
    public BoolParameter isActive = new BoolParameter(false);
    public IntParameter cascadeCount = new IntParameter(9);
    public ClampedFloatParameter renderScale = new ClampedFloatParameter(1f, 0.1f, 1f);
    public BoolParameter skyRadiance = new BoolParameter(false);
    public ColorParameter skyColor = new ColorParameter(new Color(0.2f, 0.5f, 1f), true, false, true);
    public ColorParameter sunColor = new ColorParameter(new Color(1f, 0.7f, 0.1f) * 10, true, false, true);
    public ClampedFloatParameter sunAngle = new ClampedFloatParameter(2f, 0f, 6.28f);
}
