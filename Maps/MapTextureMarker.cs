using Dalamud.Interface.Textures.TextureWraps;
using KamiToolKit.MapOverlay;
using KamiToolKit.Nodes;

namespace HuntHelperEvolved;

// KamiToolKit's file-path loader hides an image until an asynchronous load
// succeeds and never retries failures. Bind a ready, independently owned wrap
// instead, including its texture dimensions before the native node is shown.
public class MapTextureMarker : MapMarkerNode
{
    private readonly ImGuiImageNode _image;

    public MapTextureMarker(IDalamudTextureWrap texture)
    {
        var ownsTexture = false;
        try
        {
            _image = new ImGuiImageNode { FitTexture = true, TextureSize = texture.Size };
            _image.LoadTexture(texture);
            ownsTexture = true;
            _image.AttachNode(this);
        }
        catch
        {
            // A failed constructor never reaches the controller's ownership.
            _image?.Dispose();
            if (!ownsTexture) texture.Dispose();
            Dispose();
            throw;
        }
    }

    protected override void OnUpdate()
    {
        _image.Size = Size * MarkerScale;
        _image.Origin = _image.Size / 2f;
    }
}
