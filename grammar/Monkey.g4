grammar Monkey;

program: (functionDeclaration | statement)* mainFunction;

mainFunction: FN MAIN LPAREN RPAREN COLON VOID blockStatement;

functionDeclaration: FN IDENTIFIER LPAREN functionParameters? RPAREN COLON type blockStatement;

functionParameters: parameter (COMMA parameter)*;
parameter: IDENTIFIER COLON type;

type
    : INT
    | STRING
    | BOOL
    | CHAR
    | VOID
    | arrayType
    | hashType
    | functionType
    ;

arrayType: ARRAY LT type GT;
hashType: HASH LT type COMMA type GT;
functionType: FN LPAREN functionParameterTypes? RPAREN COLON type;
functionParameterTypes: type (COMMA type)*;

statement
    : letStatement
    | returnStatement
    | expressionStatement
    | ifStatement
    | whileStatement
    | blockStatement
    | printStatement
    ;

letStatement: LET CONST? IDENTIFIER COLON type ASSIGN expression SEMICOLON;
returnStatement: RETURN expression? SEMICOLON;
expressionStatement: expression SEMICOLON;
ifStatement: IF expression blockStatement (ELSE (ifStatement | blockStatement))?;
whileStatement: WHILE LPAREN expression RPAREN blockStatement;
blockStatement: LBRACE statement* RBRACE;
printStatement: PRINT LPAREN expression RPAREN SEMICOLON;

expression: comparison;

comparison: additionExpression ((LT | GT | LTE | GTE | EQ) additionExpression)*;

additionExpression: multiplicationExpression ((PLUS | MINUS) multiplicationExpression)*;
multiplicationExpression: elementExpression ((MULT | DIV) elementExpression)*;

elementExpression: primitiveExpression (elementAccess | callExpression)*;

elementAccess: LBRACKET expression RBRACKET;
callExpression: LPAREN expressionList? RPAREN;

primitiveExpression
    : INTEGER
    | STRING_LITERAL
    | CHAR_LITERAL
    | IDENTIFIER
    | TRUE
    | FALSE
    | LPAREN expression RPAREN
    | arrayLiteral
    | functionLiteral
    | hashLiteral
    ;

arrayLiteral: LBRACKET expressionList? RBRACKET;
functionLiteral: FN LPAREN functionParameters? RPAREN COLON type blockStatement;
hashLiteral: LBRACE hashContent (COMMA hashContent)* RBRACE;
hashContent: expression COLON expression;

expressionList: expression (COMMA expression)*;

// Tokens
FN: 'fn';
MAIN: 'main';
LET: 'let';
CONST: 'const';
RETURN: 'return';
IF: 'if';
WHILE: 'while';
ELSE: 'else';
PRINT: 'print';
INT: 'int';
STRING: 'string';
BOOL: 'bool';
CHAR: 'char';
VOID: 'void';
ARRAY: 'array';
HASH: 'hash';
TRUE: 'true';
FALSE: 'false';

PLUS: '+';
MINUS: '-';
MULT: '*';
DIV: '/';
LT: '<';
GT: '>';
LTE: '<=';
GTE: '>=';
EQ: '==';
ASSIGN: '=';
COMMA: ',';
COLON: ':';
SEMICOLON: ';';
LPAREN: '(';
RPAREN: ')';
LBRACE: '{';
RBRACE: '}';
LBRACKET: '[';
RBRACKET: ']';

INTEGER: [0-9]+;
STRING_LITERAL: '"' (~["\\] | '\\' .)* '"';
CHAR_LITERAL: '\'' (~['\\] | '\\' .) '\'';
IDENTIFIER: [a-zA-Z_][a-zA-Z_0-9]*;

LINE_COMMENT: '//' ~[\r\n]* -> skip;
BLOCK_COMMENT: '/*' .*? '*/' -> skip;
WHITESPACE: [ \t\r\n]+ -> skip;
