namespace Chainsaw.App.Field;

/// <summary>The scenes of the field (sketch ID7): how the characters behind the lock screen move.</summary>
public enum FieldSceneKind
{
    /// <summary>Long, slow waves across the page.</summary>
    Swell,

    /// <summary>Clouds that drift and fold into each other; the scene the sketch shows first.</summary>
    Drift,

    /// <summary>Contour lines of a slowly changing landscape, every fifth line stronger.</summary>
    Contour,

    /// <summary>Four soft blobs that wander and melt together.</summary>
    Mesh,
}
