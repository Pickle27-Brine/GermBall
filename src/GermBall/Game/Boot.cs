namespace GermBall.Game;

public static class Boot
{
    public static IScreen CreateTitle(App app) => new UI.TitleScreen(app);
}
