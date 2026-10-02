using System.Windows.Media;
using System.Windows.Media.Media3D;
using CoverArtSaver.Core;

namespace CoverArtSaver.Rendering;

/// <summary>
/// One cover in the 3D scene: a front quad and (optionally) a reflection quad hanging below it,
/// grouped together so a single rotate + translate moves both.
/// </summary>
internal sealed class CoverSlot
{
    private const double DefaultAspect = 0.7; // typical game box art, used until the image loads
    private static readonly Brush Placeholder = CreatePlaceholder();

    private readonly GeometryModel3D front;
    private readonly GeometryModel3D? reflection;
    private readonly AxisAngleRotation3D rotation = new(new Vector3D(0, 1, 0), 0);
    private readonly TranslateTransform3D translation = new();
    private Brush frontBrush = Placeholder;
    private Brush? reflectionBrush;
    private double lastOpacity = -1;
    private double opacity;

    public GameEntry Game { get; }

    public ModelVisual3D Visual { get; }

    /// <summary>How visible the cover is right now; the outermost covers fade out.</summary>
    public double Opacity => opacity;

    /// <summary>Whether a hit test result is this cover (not its reflection).</summary>
    public bool IsFront(Model3D model) => model == front;

    public CoverSlot(GameEntry game, bool showReflection)
    {
        Game = game;

        front = new GeometryModel3D { Material = new DiffuseMaterial(Placeholder) };
        front.BackMaterial = front.Material;

        var group = new Model3DGroup();
        group.Children.Add(front);

        if (showReflection)
        {
            reflection = new GeometryModel3D { Material = new DiffuseMaterial(Brushes.Black) };
            reflection.BackMaterial = reflection.Material;
            group.Children.Add(reflection);
        }

        SetSize(DefaultAspect, CoverTextureCache.ReflectionFraction);

        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(rotation));
        transform.Children.Add(translation);
        group.Transform = transform;

        Visual = new ModelVisual3D { Content = group };
    }

    public void ApplyTexture(CoverTexture texture)
    {
        // Each slot gets its own (unfrozen) brush so we can fade it individually.
        frontBrush = new ImageBrush(texture.Front);
        front.Material = front.BackMaterial = new DiffuseMaterial(frontBrush);

        if (reflection != null && texture.Reflection != null)
        {
            reflectionBrush = new ImageBrush(texture.Reflection);
            reflection.Material = reflection.BackMaterial = new DiffuseMaterial(reflectionBrush);
        }

        SetSize(texture.Aspect, CoverTextureCache.ReflectionFraction);
        lastOpacity = -1; // force the next ApplyPose to set opacity on the new brushes
    }

    public void ApplyPose(CoverPose pose)
    {
        rotation.Angle = pose.AngleDegrees;
        translation.OffsetX = pose.X;
        translation.OffsetZ = pose.Z;
        opacity = pose.Opacity;

        // Only touch opacity when it changes; the shared placeholder brush is frozen.
        if (Math.Abs(pose.Opacity - lastOpacity) > 0.001 && !frontBrush.IsFrozen)
        {
            frontBrush.Opacity = pose.Opacity;
            if (reflectionBrush != null)
            {
                reflectionBrush.Opacity = pose.Opacity;
            }

            lastOpacity = pose.Opacity;
        }
    }

    /// <summary>Fits the cover in a 1×1 box (tall art is 1 high, wide art is 1 wide), standing on y = 0.</summary>
    private void SetSize(double aspect, double reflectionFraction)
    {
        double width, height;
        if (aspect >= 1)
        {
            width = 1;
            height = 1 / aspect;
        }
        else
        {
            width = aspect;
            height = 1;
        }

        front.Geometry = CreateQuad(width, 0, height);
        if (reflection != null)
        {
            reflection.Geometry = CreateQuad(width, -height * reflectionFraction, 0);
        }
    }

    /// <summary>A rectangle from (-w/2, bottom) to (w/2, top) in the z = 0 plane, texture mapped upright.</summary>
    private static MeshGeometry3D CreateQuad(double width, double bottom, double top)
    {
        var half = width / 2;
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new Point3D(-half, bottom, 0), new Point3D(half, bottom, 0),
                new Point3D(half, top, 0), new Point3D(-half, top, 0),
            },
            // Texture (u, v): v = 0 is the top of the image, so the top corners get v = 0.
            TextureCoordinates = new PointCollection
            {
                new System.Windows.Point(0, 1), new System.Windows.Point(1, 1),
                new System.Windows.Point(1, 0), new System.Windows.Point(0, 0),
            },
            TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
        };
        mesh.Freeze();
        return mesh;
    }

    private static Brush CreatePlaceholder()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x22, 0x24, 0x28));
        brush.Freeze();
        return brush;
    }
}
