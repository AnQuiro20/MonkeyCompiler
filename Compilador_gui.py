#!/usr/bin/env python3
"""
Monkey Compiler GUI frontend (tkinter).

Provides a small windowed editor with tabs for Errors, Output and IR and a
"Compilar/Run" button that invokes the C# backend.

Usage: run without arguments to open the editor, or pass a .monkey file path to
open it on startup.

Requires: Python 3 with tkinter (on Debian/Ubuntu: sudo apt install python3-tk)
"""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import threading
import tkinter as tk
from tkinter import ttk, filedialog

DOTNET_PROJECT = 'src/Monkey.Frontend/Frontend.csproj'


class CompilerGUI(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title('Monkey Compiler - GUI')
        self.geometry('1000x700')
        self.current_file: str | None = None
        self._build_ui()

    def _build_ui(self) -> None:
        menubar = tk.Menu(self)
        filemenu = tk.Menu(menubar, tearoff=0)
        filemenu.add_command(label='New', command=self.new_file, accelerator='Ctrl+N')
        filemenu.add_command(label='Open...', command=self.open_file, accelerator='Ctrl+O')
        filemenu.add_command(label='Save', command=self.save_file, accelerator='Ctrl+S')
        filemenu.add_command(label='Save As...', command=self.save_as)
        filemenu.add_separator()
        filemenu.add_command(label='Exit', command=self.quit)
        menubar.add_cascade(label='File', menu=filemenu)
        self.config(menu=menubar)

        toolbar = ttk.Frame(self)
        run_btn = ttk.Button(toolbar, text='Compilar/Run', command=self.on_run)
        run_btn.pack(side=tk.LEFT, padx=4, pady=4)
        toolbar.pack(fill=tk.X)

        paned = ttk.Panedwindow(self, orient=tk.HORIZONTAL)
        paned.pack(fill=tk.BOTH, expand=1)

        editor_frame = ttk.Frame(paned)
        self.editor = tk.Text(editor_frame, wrap='none', undo=True)
        self.editor.pack(fill=tk.BOTH, expand=1)
        yscroll = ttk.Scrollbar(editor_frame, orient=tk.VERTICAL, command=self.editor.yview)
        xscroll = ttk.Scrollbar(editor_frame, orient=tk.HORIZONTAL, command=self.editor.xview)
        self.editor.configure(yscrollcommand=yscroll.set, xscrollcommand=xscroll.set)
        yscroll.pack(side=tk.RIGHT, fill=tk.Y)
        xscroll.pack(side=tk.BOTTOM, fill=tk.X)
        paned.add(editor_frame, weight=3)

        right_frame = ttk.Frame(paned)
        tabs = ttk.Notebook(right_frame)
        self.err_view = tk.Text(tabs, wrap='word', foreground='red', state='normal')
        self.out_view = tk.Text(tabs, wrap='word', state='normal')
        self.ir_view = tk.Text(tabs, wrap='none', state='normal')
        tabs.add(self.err_view, text='Errores')
        tabs.add(self.out_view, text='Salida')
        tabs.add(self.ir_view, text='IR')
        tabs.pack(fill=tk.BOTH, expand=1)
        paned.add(right_frame, weight=1)

        self.bind_all('<Control-s>', lambda e: self.save_file())
        self.bind_all('<Control-o>', lambda e: self.open_file())
        self.bind_all('<Control-n>', lambda e: self.new_file())

        sample = (
            "fn main(): void {\n"
            "    let i: int = 0;\n"
            "    while (i < 3) {\n"
            "        print(i);\n"
            "        let i: int = i + 1;\n"
            "    }\n"
            "}\n"
        )
        self.editor.insert('1.0', sample)
        # Status bar for line/column
        self.status_var = tk.StringVar()
        status = ttk.Label(self, textvariable=self.status_var, anchor='w')
        status.pack(fill=tk.X, side=tk.BOTTOM)

        # Update cursor position on common events
        self.editor.bind('<KeyRelease>', lambda e: self._update_cursor_pos())
        self.editor.bind('<ButtonRelease-1>', lambda e: self._update_cursor_pos())
        # initialize status
        self._update_cursor_pos()

    def new_file(self) -> None:
        if self._maybe_save():
            self.editor.delete('1.0', tk.END)
            self.current_file = None
            self.title('Monkey Compiler - GUI')
            self._update_cursor_pos()

    def open_file(self) -> None:
        if not self._maybe_save():
            return
        fp = filedialog.askopenfilename(title='Open Monkey source', filetypes=[('Monkey', '*.monkey'), ('All files', '*.*')])
        if not fp:
            return
        with open(fp, 'r', encoding='utf-8') as f:
            self.editor.delete('1.0', tk.END)
            self.editor.insert('1.0', f.read())
        self.current_file = fp
        self.title(f'Monkey Compiler - {os.path.basename(fp)}')
        self._update_cursor_pos()

    def save_file(self) -> bool:
        if self.current_file:
            with open(self.current_file, 'w', encoding='utf-8') as f:
                f.write(self.editor.get('1.0', tk.END))
            return True
        return self.save_as()

    def save_as(self) -> bool:
        fp = filedialog.asksaveasfilename(title='Save Monkey source', defaultextension='.monkey', filetypes=[('Monkey', '*.monkey'), ('All files', '*.*')])
        if not fp:
            return False
        with open(fp, 'w', encoding='utf-8') as f:
            f.write(self.editor.get('1.0', tk.END))
        self.current_file = fp
        self.title(f'Monkey Compiler - {os.path.basename(fp)}')
        return True

    def _maybe_save(self) -> bool:
        # Minimal always-yes save policy for this simple editor.
        return True

    def on_run(self) -> None:
        if self.current_file is None:
            fd, path = tempfile.mkstemp(suffix='.monkey')
            os.close(fd)
            with open(path, 'w', encoding='utf-8') as f:
                f.write(self.editor.get('1.0', tk.END))
            temp_created = True
        else:
            path = self.current_file
            with open(path, 'w', encoding='utf-8') as f:
                f.write(self.editor.get('1.0', tk.END))
            temp_created = False

        self.err_view.config(state='normal')
        self.err_view.delete('1.0', tk.END)
        self.out_view.config(state='normal')
        self.out_view.delete('1.0', tk.END)
        self.ir_view.config(state='normal')
        self.ir_view.delete('1.0', tk.END)

        threading.Thread(target=self._run_backend, args=(path, temp_created), daemon=True).start()

    def _run_backend(self, path: str, temp_created: bool) -> None:
        cmd = ['dotnet', 'run', '--project', DOTNET_PROJECT, '--', '--compile-file', path]
        try:
            proc = subprocess.run(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding='utf-8',
                errors='ignore'
            )

        except FileNotFoundError:
            self._append_error('dotnet not found. Please install .NET SDK and ensure dotnet is in PATH.')
            if temp_created:
                try:
                    os.remove(path)
                except Exception:
                    pass
            return

        out = proc.stdout
        err = proc.stderr
        errs, outp, ir = self._split_sections(out)

        # Determine whether there are errors from stdout sections, stderr, or non-zero return code.
        has_stderr = bool(err and err.strip())
        has_errors_section = len(errs) > 0
        has_return_error = proc.returncode != 0

        if has_errors_section:
            # Errors reported in stdout sections (preferred)
            joined = '\n'.join(errs)
            self._append_error(joined)
            # Try to find a mapped source line in the error text and highlight it
            m = None
            try:
                import re
                mm = re.search(r"source line\s+(\d+)", joined, re.IGNORECASE)
                if mm:
                    m = int(mm.group(1))
            except Exception:
                m = None
            if m:
                # highlight line (GUI runs on main thread via after)
                self._highlight_line(m)
        elif has_stderr or has_return_error:
            # If there is stderr output or a non-zero return code, show stderr (if any)
            if has_stderr:
                self._append_error('STDERR:\n' + err)
                # try to parse source line from stderr too
                try:
                    import re
                    mm = re.search(r"source line\s+(\d+)", err, re.IGNORECASE)
                    if mm:
                        self._highlight_line(int(mm.group(1)))
                except Exception:
                    pass
            else:
                # No stderr text but non-zero exit: show a generic message
                self._append_error(f'Error: backend exited with code {proc.returncode}')
        else:
            self._append_error('Sin errores.')
            # clear any previous highlight
            self._clear_highlight()

        # Always display output and IR if present, even when there are errors.
        if outp:
            self._append_output(outp)
        if ir:
            self._append_ir('\n'.join(ir))

        if temp_created:
            try:
                os.remove(path)
            except Exception:
                pass

    def _split_sections(self, text: str) -> tuple[list[str], str, list[str]]:
        lines = text.splitlines()
        errs: list[str] = []
        out: list[str] = []
        ir: list[str] = []
        section = None
        for l in lines:
            if l.strip() == '=== Errores ===':
                section = 'errs'
                continue
            if l.strip() == '=== Salida ===':
                section = 'out'
                continue
            if l.strip() == '=== IR ===':
                section = 'ir'
                continue
            if section == 'errs':
                errs.append(l)
            elif section == 'out':
                out.append(l)
            elif section == 'ir':
                ir.append(l)
        return errs, '\n'.join(out), ir

    def _append_error(self, text: str) -> None:
        def cb() -> None:
            self.err_view.insert(tk.END, text + '\n')
            self.err_view.see(tk.END)

        self.err_view.after(0, cb)

    def _clear_highlight(self) -> None:
        def cb() -> None:
            try:
                self.editor.tag_delete('error_line')
            except Exception:
                pass
        self.editor.after(0, cb)

    def _update_cursor_pos(self, event: object | None = None) -> None:
        """Update the status bar with the current line and column of the insert cursor."""
        try:
            idx = self.editor.index(tk.INSERT)
            # idx is 'line.column' where column is 0-based
            line, col = idx.split('.')
            col_no = int(col) + 1
            self.status_var.set(f'Ln {line}, Col {col_no}')
        except Exception:
            try:
                self.status_var.set('')
            except Exception:
                pass

    def _highlight_line(self, line_no: int) -> None:
        def cb() -> None:
            try:
                # Remove existing tag
                try:
                    self.editor.tag_delete('error_line')
                except Exception:
                    pass
                start = f"{line_no}.0"
                end = f"{line_no}.end"
                self.editor.tag_add('error_line', start, end)
                self.editor.tag_config('error_line', background='#ffdddd')
                self.editor.see(start)
            except Exception:
                pass
        self.editor.after(0, cb)

    def _append_output(self, text: str) -> None:
        def cb() -> None:
            self.out_view.insert(tk.END, text + '\n')
            self.out_view.see(tk.END)

        self.out_view.after(0, cb)

    def _append_ir(self, text: str) -> None:
        def cb() -> None:
            self.ir_view.insert(tk.END, text + '\n')
            self.ir_view.see(tk.END)

        self.ir_view.after(0, cb)


def main() -> None:
    app = CompilerGUI()
    if len(sys.argv) >= 2:
        fp = sys.argv[1]
        if os.path.isfile(fp):
            with open(fp, 'r', encoding='utf-8') as f:
                app.editor.delete('1.0', tk.END)
                app.editor.insert('1.0', f.read())
            app.current_file = fp
            app.title(f'Monkey Compiler - {os.path.basename(fp)}')
    app.mainloop()


if __name__ == '__main__':
    try:
        main()
    except Exception as e:
        print('Error launching GUI:', e)
        sys.exit(1)
