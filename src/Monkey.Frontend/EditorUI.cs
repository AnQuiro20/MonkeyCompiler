using System;
using Terminal.Gui;

namespace Monkey.Frontend
{
    public static class EditorUI
    {
        public static void Launch()
        {
            Application.Init();
            var top = Application.Top;

            var win = new Window("Monkey Editor - Compilar/Run") { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill() };
            top.Add(win);

            // Editor area
            var editor = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(60),
                Height = Dim.Percent(70),
                WordWrap = true
            };

            // Preload with test program
            editor.Text = @"fn main(): void {
    let i: int = 0;
    while (i < 3) {
        print(i);
        let i: int = i + 1;
    }
}";

            win.Add(editor);

            // Errors pane
            var errorsLabel = new Label("Errores:") { X = Pos.Right(editor) + 1, Y = 0 };
            var errorsView = new TextView() { X = Pos.Right(editor) + 1, Y = 1, Width = Dim.Fill(), Height = Dim.Percent(35), ReadOnly = true };
            win.Add(errorsLabel, errorsView);

            // Output pane
            var outputLabel = new Label("Salida:") { X = 0, Y = Pos.Bottom(editor) + 1 };
            var outputView = new TextView() { X = 0, Y = Pos.Bottom(editor) + 2, Width = Dim.Fill(), Height = Dim.Fill() - 3, ReadOnly = true };
            win.Add(outputLabel, outputView);

            // Compile/Run button
            var runBtn = new Button("Compilar/Run") { X = Pos.Right(editor) - 10, Y = Pos.Bottom(editor) - 1 };
            runBtn.Clicked += () =>
            {
                errorsView.Text = string.Empty;
                outputView.Text = string.Empty;
                var source = editor.Text.ToString();
                var (errs, outp, instr) = CompilerService.CompileAndRun(source);
                if (errs.Count > 0)
                {
                    errorsView.Text = string.Join('\n', errs);
                }
                else
                {
                    errorsView.Text = "Sin errores.";
                }

                outputView.Text = outp + "\n\n-- IR --\n" + string.Join('\n', instr);
            };

            win.Add(runBtn);

            // Menu bar
            var menu = new MenuBar(new MenuBarItem[] {
                new MenuBarItem ("_File", new MenuItem [] {
                    new MenuItem ("_Quit", "Quit", () => { Application.RequestStop(); })
                })
            });
            top.Add(menu);

            Application.Run();
        }
    }
}
