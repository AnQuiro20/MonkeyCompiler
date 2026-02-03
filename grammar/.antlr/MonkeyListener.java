// Generated from c:/Users/begin/OneDrive - Estudiantes ITCR/2S 2025/Compiladores e interpretes/proyecto/MonkeyCompiler/MonkeyCompiler/grammar/Monkey.g4 by ANTLR 4.13.1
import org.antlr.v4.runtime.tree.ParseTreeListener;

/**
 * This interface defines a complete listener for a parse tree produced by
 * {@link MonkeyParser}.
 */
public interface MonkeyListener extends ParseTreeListener {
	/**
	 * Enter a parse tree produced by {@link MonkeyParser#program}.
	 * @param ctx the parse tree
	 */
	void enterProgram(MonkeyParser.ProgramContext ctx);
	/**
	 * Exit a parse tree produced by {@link MonkeyParser#program}.
	 * @param ctx the parse tree
	 */
	void exitProgram(MonkeyParser.ProgramContext ctx);
	/**
	 * Enter a parse tree produced by {@link MonkeyParser#statement}.
	 * @param ctx the parse tree
	 */
	void enterStatement(MonkeyParser.StatementContext ctx);
	/**
	 * Exit a parse tree produced by {@link MonkeyParser#statement}.
	 * @param ctx the parse tree
	 */
	void exitStatement(MonkeyParser.StatementContext ctx);
	/**
	 * Enter a parse tree produced by {@link MonkeyParser#expressionStatement}.
	 * @param ctx the parse tree
	 */
	void enterExpressionStatement(MonkeyParser.ExpressionStatementContext ctx);
	/**
	 * Exit a parse tree produced by {@link MonkeyParser#expressionStatement}.
	 * @param ctx the parse tree
	 */
	void exitExpressionStatement(MonkeyParser.ExpressionStatementContext ctx);
	/**
	 * Enter a parse tree produced by {@link MonkeyParser#expression}.
	 * @param ctx the parse tree
	 */
	void enterExpression(MonkeyParser.ExpressionContext ctx);
	/**
	 * Exit a parse tree produced by {@link MonkeyParser#expression}.
	 * @param ctx the parse tree
	 */
	void exitExpression(MonkeyParser.ExpressionContext ctx);
	/**
	 * Enter a parse tree produced by {@link MonkeyParser#literal}.
	 * @param ctx the parse tree
	 */
	void enterLiteral(MonkeyParser.LiteralContext ctx);
	/**
	 * Exit a parse tree produced by {@link MonkeyParser#literal}.
	 * @param ctx the parse tree
	 */
	void exitLiteral(MonkeyParser.LiteralContext ctx);
}