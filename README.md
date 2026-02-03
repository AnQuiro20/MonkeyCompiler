# 🐒 MonkeyCompiler

Compilador educativo para el lenguaje **Monkey**, desarrollado como proyecto académico del curso **Compiladores e Intérpretes** en el Instituto Tecnológico de Costa Rica (TEC).

El proyecto implementa **todas las fases fundamentales de un compilador**, desde el análisis léxico y sintáctico hasta la **generación y ejecución de código intermedio**, incluyendo una **máquina virtual propia** y una **interfaz gráfica** para facilitar su uso.

---

## 📌 Descripción general

**Monkey** es un lenguaje de programación educativo diseñado para ilustrar los procesos internos de un compilador moderno. Su sintaxis está inspirada en lenguajes imperativos como **C, Go y Python**, y permite trabajar con:

- Variables y tipos básicos  
- Funciones con parámetros y retorno  
- Estructuras de control (`if`, `else`, `while`)  
- Expresiones aritméticas y booleanas  

El compilador traduce programas Monkey a un **código intermedio (IR)** basado en una *stack machine*, el cual es ejecutado por una **máquina virtual personalizada**.  
Adicionalmente, el proyecto incluye una **GUI desarrollada en Python con Tkinter**, que permite escribir, compilar y ejecutar programas desde un mismo entorno.

---

## 🧠 Arquitectura del compilador

El compilador sigue una arquitectura modular dividida en las siguientes fases:

1. **Análisis léxico**  
   - Implementado con **ANTLR4**
   - Reconoce tokens, literales y palabras reservadas

2. **Análisis sintáctico**  
   - Generado automáticamente con ANTLR4
   - Construcción del Árbol de Sintaxis Abstracta (AST) usando el **Visitor Pattern**

3. **Análisis semántico**  
   - Tabla de símbolos jerárquica
   - Verificación de tipos y ámbitos
   - Validación de declaraciones y llamadas a funciones

4. **Generación de código intermedio (IR)**  
   - Traducción del AST a instrucciones tipo *stack machine*
   - IR ejecutable y legible para depuración

5. **Ejecución**  
   - Máquina virtual propia (`VirtualMachine`)
   - Soporte para llamadas a funciones, control de flujo y operaciones aritméticas

6. **Backend alternativo (CIL)**  
   - Generación dinámica de código usando `Reflection.Emit`
   - Posibilidad de ejecutar código CIL en memoria o generar un ejecutable de consola (`.exe`)

---

## 🧪 Tipos de datos soportados

- `IntType`
- `StringType`
- `BoolType`
- `VoidType`

---

## ✅ Validaciones semánticas

El compilador detecta y reporta errores como:

- Uso de variables no declaradas
- Tipos incompatibles en expresiones
- Número o tipo incorrecto de parámetros en funciones
- Tipos de retorno inválidos

Los errores incluyen **línea, columna y descripción**, deteniendo la compilación antes de la generación de código.

---

## 🖥️ Interfaz gráfica (GUI)

La interfaz gráfica fue desarrollada en **Python 3 con Tkinter** y se comunica con el compilador en C# mediante **procesos del sistema**.

La GUI muestra de forma separada:

- `=== Errores ===`
- `=== Salida ===`
- `=== IR ===`

Esto permite una experiencia cercana a un entorno real de desarrollo.

---

## 📥 Ejemplo de programa Monkey

```monkey
fn main(): void {
    let x: int = 10;
    let y: int = 20;
    let z: int = x + y * 2;
    print(z);
}
````

### Código intermedio generado (IR)

```
FUNC_START main
DECLARE x : IntType
LOAD_CONST 10
STORE x

DECLARE y : IntType
LOAD_CONST 20
STORE y

DECLARE z : IntType
LOAD_VAR x
LOAD_VAR y
LOAD_CONST 2
MUL
ADD
STORE z

LOAD_VAR z
PRINT
FUNC_END main
```

### Salida del programa

```
50
```

---

## 🛠️ Tecnologías utilizadas

| Componente                   | Tecnología              |
| ---------------------------- | ----------------------- |
| Lenguaje principal           | C# (.NET 8.0)           |
| Análisis léxico y sintáctico | ANTLR4                  |
| IDE                          | Visual Studio / VS Code |
| Interfaz gráfica             | Python 3 + Tkinter      |
| Backend alternativo          | CIL / Reflection.Emit   |

---

## 📊 Resultados obtenidos

* Analizador léxico funcional
* Analizador sintáctico con AST
* Análisis semántico completo
* Generación de IR ejecutable
* Máquina virtual propia
* Soporte para funciones y estructuras de control
* Backend alternativo con generación de ejecutables
* Interfaz gráfica integrada

---

## 🎓 Contexto académico

* **Curso:** Compiladores e Intérpretes
* **Institución:** Instituto Tecnológico de Costa Rica (TEC) – Campus San Carlos
* **Semestre:** II Semestre, 2025

### Autores

* **Andrés Quirós Rojas**
* **Erick Durán Maroto**

---

## 📚 Bibliografía

* Aho, A. V., Lam, M. S., Sethi, R., & Ullman, J. D. *Compilers: Principles, Techniques, and Tools*
* Nisan, N., & Schocken, S. *The Elements of Computing Systems*
* Nystrom, R. *Crafting Interpreters*
* Louden, K. C. *Compiler Construction*
* Hecht, M. *Introduction to Compilers and Language Design*

---

## 🚀 Estado del proyecto

✔️ Proyecto finalizado y funcional

✔️ Cumple con los requerimientos académicos

✔️ Diseñado con fines educativos y extensibles


