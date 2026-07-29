using Editor.Client.Services;

namespace Editor.Client.Tests;

public sealed class EditorShellUiStateTests
{
    [Test]
    public void Shell_State_Is_Memory_Only_And_Idempotent()
    {
        var state = new EditorShellUiState();
        var changes = 0;
        state.Changed += () => changes++;

        state.SetViewportWidth(1440);
        state.SetViewportWidth(1440);
        state.SetViewportWidth(1024);

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(2));
            Assert.That(state.UsesCompactLayout, Is.True);
            Assert.That(typeof(EditorShellUiState).GetProperties(), Has.None.Matches<System.Reflection.PropertyInfo>(property => property.Name.Contains("Document", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Test]
    public void Drawer_Transitions_Keep_A_Single_Valid_Mode()
    {
        var state = new EditorShellUiState();

        Assert.Multiple(() =>
        {
            Assert.That(state.TryOpenDrawer("unknown"), Is.False);
            Assert.That(state.ActiveDrawer, Is.Null);
            Assert.That(state.ToggleDrawer(EditorDrawerMode.Assets), Is.True);
            Assert.That(state.ActiveDrawer, Is.EqualTo(EditorDrawerMode.Assets));
            Assert.That(state.ToggleDrawer(EditorDrawerMode.Layers), Is.True);
            Assert.That(state.ActiveDrawer, Is.EqualTo(EditorDrawerMode.Layers));
        });

        state.CloseDrawer();
        Assert.That(state.ActiveDrawer, Is.Null);
    }
}
