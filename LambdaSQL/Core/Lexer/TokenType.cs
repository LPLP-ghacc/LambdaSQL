namespace LambdaSQL.Core.Lexer;

public enum TokenType
{
    // Literals
    Integer,
    Float,
    String,
    Bool,
    Null,
    Identifier,

    // Keywords
    Select,
    From,
    Where,
    Insert,
    Into,
    Values,
    Update,
    Set,
    Delete,
    Create,
    Drop,
    Table,
    Order,
    By,
    Asc,
    Desc,
    Limit,
    Group,
    Join,
    Inner,
    Left,
    On,
    And,
    Or,
    Not,
    In,
    Like,
    Is,
    As,
    Distinct,
    Having,

    // Types
    TypeInt,
    TypeBigInt,
    TypeFloat,
    TypeText,
    TypeBool,

    // Operators
    Equals,         // =
    NotEquals,      // != or <>
    Less,           // <
    LessOrEqual,    // <=
    Greater,        // >
    GreaterOrEqual, // >=
    Plus,           // +
    Minus,          // -
    Star,           // *
    Slash,          // /
    Percent,        // %

    // Punctuation
    LeftParen,      // (
    RightParen,     // )
    Comma,          // ,
    Semicolon,      // ;
    Dot,            // .

    // Special
    Eof,
}
