using GermBall.Render;
namespace GermBall.UI;
public sealed class TitleScreen : IScreen
{
    readonly App app;
    public TitleScreen(App app) { this.app = app; }
    public void Update(float dt) { }
    public void Render(float dt) { }
    public void DrawUI(Draw2D ui, float dt) { }
}
