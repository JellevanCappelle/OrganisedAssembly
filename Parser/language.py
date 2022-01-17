from arpeggio import Optional, ZeroOrMore, OneOrMore, Sequence, Not, EOF
from arpeggio import RegExMatch as regex
from arpeggio import ParserPython
from arpeggio import NonTerminal
from arpeggio.export import PTDOTExporter
import sys
import json

# load instructions and other keywords
with open("keywords/keywords.txt", "r") as db:
    keywords = db.read().split('\n')
with open("keywords/instructions.txt", "r") as db:
    instructionKeyword = db.read().split('\n')
instructionKeyword = [i for i in instructionKeyword if not i == "callf" and not i.startswith("rep")]

with open("keywords/sse instructions.txt", "r") as sse:
	sseInstructionKeyword = list(sse)

# register types
GPRs = []
for l in "abcd":
    GPRs += [l + 'l', l + 'h', l + 'x', f"e{l}x", f"r{l}x"]
for r in ["di", "si", "sp", "bp"]:
    GPRs += [r, 'e' + r, 'r' + r]
for i in range(8, 16):
    GPRs.append(f"r{i}")
    GPRs.append(f"r{i}d")
    GPRs.append(f"r{i}w")

SRs = [l + 's' for l in "cdefg"]

CRs = ["cr" + str(i) for i in range(8)]

SSERs = ["xmm" + str(i) for i in range(16)]

conditionSuffixes = ["e", "z", "l", "g", "a", "b", "le", "ge", "be", "ae", "c", "s", "p", "o"]
ifKeywords = []
forKeywords = []
whileKeywords = []
for suf in conditionSuffixes:
    ifKeywords += ["if" + suf, "ifn" + suf]
    forKeywords += ["for" + suf, "forn" + suf]
    whileKeywords += ["while" + suf, "whilen" + suf]
    instructionKeyword += ["set" + suf, "setn" + suf]
	
sizeKeywords = ["byte", "word", "dword", "qword"]
stringSizeKeywords = [kwd + 's' for kwd in sizeKeywords]

nonIdentifiers = instructionKeyword + ifKeywords + forKeywords + whileKeywords + sizeKeywords + stringSizeKeywords + ["constant", "string", "cstring", "function", "method", "using", "namespace", "ref", "enum", "struct", "sizeof", "alias", "binary"]


# define language rules

# registers
def gpRegister(): return GPRs
def segRegister(): return SRs
def controlRegister(): return CRs
def register(): return [gpRegister, segRegister, controlRegister]
def sseRegister(): return SSERs

# numbers
def decimal(): return regex('[0-9]+d?')
def hexadecimal(): return [regex('0x[0-9a-fA-F]+'), regex('[0-9][0-9a-fA-F]*h')]
def binary(): return [regex('0b[01]+'), regex('[01]+b')]
def singleQuotedString(): return regex(r"'([^'\\]|\\.)*'")
def doubleQuotedString(): return regex(r'"([^"\\]|\\.)*"')
def number(): return [hexadecimal, binary, decimal, singleQuotedString]

# identifier
def name(): return Not(nonIdentifiers), regex('[a-zA-Z_][a-zA-Z0-9_]*')
def namePath(): return name, ZeroOrMore('.', name)
def templateParameters(): return '<', identifierPath, ZeroOrMore(',', identifierPath), '>'
def templateDeclarationParameters(): return '<', name, ZeroOrMore(',', name), '>' # TODO: allow syntax for specifying parameter types
def templateName(): return name, Optional(templateDeclarationParameters)
def identifier(): return name, Optional(templateParameters)
def identifierPath(): return identifier, ZeroOrMore('.', identifier)

# expression # TODO: differentiate between expressions that can or can't contain registers (i.e. effective addresses or immediates)
def sizeof(): return "sizeof", '(', identifierPath, ')'
def exprValue(): return [number, register, sizeof, identifierPath]
def binaryOperator(): return ['+', '-', '*', '/', '^', '&', '|', '<<', '>>']
def unaryOperator(): return ['-', '~']
def exprTerm(): return Optional(unaryOperator), [('(', expr, ')'), exprValue]
def expr(): return exprTerm, ZeroOrMore(binaryOperator, exprTerm)

# operands
def baseOrOffset(): return [gpRegister, expr]
def offsetMultiplier(): return baseOrOffset, Optional('*', expr) # TODO: redo this (and expressions in general) properly, taking precedence rules into account
def baseOffsetMultiplier(): return baseOrOffset, Optional(['+', '-'], offsetMultiplier)
def address(): return baseOffsetMultiplier # TODO: fix baseOffsetMultiplier
def segAddress(): return Optional(segRegister, ':'), address
def sizeSpecifier(): return sizeKeywords
def memReference(): return Optional(sizeSpecifier), '[', segAddress, ']'
def immediate(): return Optional(sizeSpecifier), expr # can also be an aliased register!
def operand(): return [register, memReference, aliasDecl, immediate]
def operandList(): return operand, ZeroOrMore(',', operand)

# sse instructions
def sseMemReference(): return '[', segAddress, ']'
def sseOperand(): return [sseRegister, sseMemReference]
def sseOpcode(): return sseInstructionKeyword
def sseInstruction(): return sseOpcode, sseOperand, ZeroOrMore(',', sseOperand)

# statements
def opcode(): return instructionKeyword
def repPrefix(): return ["rep", "repe", "repne", "repnz", "repz"]
def lockPrefix(): return "lock"
def instruction(): return Optional(lockPrefix), Optional(repPrefix), opcode, Optional(operandList)
def label(): return name, ':'
def comment(): return regex('#.*')
def statement(): return Optional(label), Optional([instruction, sseInstruction, controlFlow, abiReturn, abiCall, methodCall, declaration]), Optional(comment)
def emptyStatement(): return Optional(comment)

# variables and constants
def sizeOrType(): return [sizeSpecifier, identifierPath]
def exprList(): return expr, ZeroOrMore(',', expr)
def varAssignment(): return '=', expr
def variableDecl(): return sizeOrType, '[', name, ']', Optional(varAssignment)
def dataStringType(): return stringSizeKeywords
def dataStringDecl(): return dataStringType, '[', name, ']', '=', exprList
def textStringDecl(): return "string", '[', name, ']', '=', doubleQuotedString
def cStringDecl(): return "cstring", '[', name, ']', '=', singleQuotedString
def fileDecl(): return "binary", '[', name, ']', ':', doubleQuotedString
def constantDecl(): return "constant", name, '=', expr
def arrayDecl(): return "byte", '(', expr, ')', '[', name, ']'
def aliasDecl(): return "alias", name, '=', gpRegister
def declaration(): return [variableDecl, dataStringDecl, textStringDecl, cStringDecl, constantDecl, arrayDecl, aliasDecl, fileDecl]

# enums
def enumAssignment(): return name, '=', expr
def enumStatement(): return Optional(enumAssignment), Optional(comment)
def enumBody(): return '{', enumStatement, ZeroOrMore('\n', enumStatement), '}'
def enum(): return "enum", name, Optional(emptySpace), enumBody, Optional(comment)

# ABI calls
def memArgument(): return Optional(sizeOrType), '[', segAddress, ']'
def immArgument(): return Optional(sizeSpecifier), expr
def refArgument(): return "ref", [('[', segAddress, ']'), singleQuotedString, doubleQuotedString]
def argument(): return [gpRegister, memArgument, refArgument, immArgument]
def returnTarget(): return [gpRegister, memArgument, aliasDecl, name] # only aliased registers should be allowed for 'name'
def returnTargetList(): return returnTarget, ZeroOrMore(',', returnTarget)
def argumentList(): return argument, ZeroOrMore(',', argument)
def abiAssignment(): return returnTargetList, '='
def abiCall(): return Optional(abiAssignment), identifierPath, '(', Optional(argumentList), ')' # TODO: allow registers and memory operands to serve as function pointers
def abiReturn(): return "return", Optional(argumentList)

# functions
def parameter(): return sizeOrType, '[', name, ']'
def parameterList(): return parameter, ZeroOrMore(',', parameter)
def functionDeclaration(): return "function", templateName, '(', Optional(parameterList), ')'
def emptySpace(): return OneOrMore(emptyStatement, '\n')
def localCode(): return statement, ZeroOrMore('\n', statement)
def localBody(): return '{', localCode, '}'
def function(): return functionDeclaration, Optional(emptySpace), localBody, Optional(comment)

# structs
def structVariableDecl(): return sizeOrType, '[', name, ']'
def structField(): return [structVariableDecl, constantDecl, arrayDecl]
def staticKeyword(): return "static"
def structMethodDecl(): return Optional(staticKeyword), "method", templateName, '(', Optional(parameterList), ')'
def structMethod(): return structMethodDecl, Optional(emptySpace), localBody, Optional(comment)
def structStatement(): return Optional([structField, structMethod]), Optional(comment)
def structBody(): return '{', structStatement, ZeroOrMore('\n', structStatement), '}'
def struct(): return "struct", templateName, Optional(emptySpace), structBody, Optional(comment)
def regPointerCast(): return '(', gpRegister, "as", identifierPath, ')'
def memPointerCast(): return '[', segAddress, "as", identifierPath, ']'
def directPointer(): return '[', identifierPath, ']'
def structReference(): return [directPointer, regPointerCast, memPointerCast]
def methodCall(): return Optional(abiAssignment), structReference, '.', identifierPath, '(', Optional(argumentList), ')'

# control flow
def oneliner(): return [instruction, controlFlow, abiReturn, abiCall]
def initialiser(): return [instruction, abiCall, declaration]
def condition(): return [instruction, abiCall]
def repeatable(): return [instruction, controlFlow, abiCall]
def ifKeyword(): return ifKeywords
def ifBody(): return [localBody, oneliner]
def ifStatement(): return ifKeyword, '(', Optional(condition), ')', Optional(emptySpace), ifBody, Optional(comment), Optional('\n', elseStatement)
def elseStatement(): return "else", Optional(emptySpace), ifBody, Optional(comment)
def whileKeyword(): return whileKeywords
def loopBody(): return [localBody, repeatable]
def whileLoop(): return whileKeyword, '(', Optional(condition), ')', Optional(emptySpace), loopBody, Optional(comment)
def doWhileLoop(): return "do", Optional(emptySpace), loopBody, Optional(emptySpace), whileKeyword, '(', Optional(condition), ')', Optional(comment)
def forKeyword(): return forKeywords
def forLoop(): return forKeyword, '(', Optional(initialiser), ';', Optional(condition), ';', Optional(repeatable), ')', Optional(emptySpace), loopBody, Optional(comment)
def doForLoop(): return "do", Optional(emptySpace), loopBody, Optional(emptySpace), forKeyword, '(', Optional(initialiser), ';', Optional(condition), ';', Optional(repeatable), ')', Optional(comment)
def breakStatement(): return "break"
def continueStatement(): return "continue"
def controlFlow(): return [breakStatement, continueStatement, ifStatement, whileLoop, doWhileLoop, forLoop, doForLoop]

# namespaces
def globalStatement(): return [namespace, enum, struct, function, statement, '']
def globalCode(): return globalStatement, ZeroOrMore('\n', globalStatement)
def namespaceDeclaration(): return 'namespace', namePath
def namespaceBody(): return '{', globalCode, '}'
def namespace(): return namespaceDeclaration, Optional(emptySpace), namespaceBody, Optional(comment)

# file structure
def using(): return "using", namePath, Optional(comment), '\n'
def usingList(): return using, ZeroOrMore(Optional(emptySpace), using)
def program(): return Optional(emptySpace), Optional(usingList), globalCode, EOF


parser = ParserPython(program, ws = "\t\r ", autokwd = True, reduce_tree = False, memoization = True)
with open(input()) as file:
    code = file.read()
try:
    result = parser.parse(code)
except Exception as e:
    print(e, file=sys.stderr)
    sys.exit()

def semiSerialise(nonTerminal):
    rule = nonTerminal.rule_name
    children = []
    for i in range(len(nonTerminal)):
        child = nonTerminal[i]
        if isinstance(child, NonTerminal):
            child = semiSerialise(child)
        else:
            if child.rule_name != "":
                child = (child.rule_name, [str(child)], parser.pos_to_linecol(child.position))
            else:
                child = str(child)
        children.append(child)
    position = parser.pos_to_linecol(nonTerminal.position)
    return (rule, children, position)

def escape(string):
    chars = '\\"\t\n'
    replacements = '\\"tn'
    for i, c in enumerate(chars):
        string = string.replace(c, f"\\{replacements[i]}")
    return f'"{string}"'

def serialise(tree):
    if isinstance(tree, tuple):
        rule = tree[0]
        children = list(map(serialise, tree[1]))
        line, column = tree[2]
        position = f"{line},{column}"
        return f'{{"{rule}":[{",".join(children + [position])}]}}'
    else:
        return escape(tree)

tree = semiSerialise(result)
tree_json = serialise(tree)
print(tree_json)
        


    
