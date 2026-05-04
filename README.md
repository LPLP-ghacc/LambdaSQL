# LambdaSQL

A lightweight, self-contained relational database engine written entirely in C#.
LambdaSQL runs natively on **Windows** and **Linux**, requires no external dependencies,
and works in two modes: **embedded** (as a library inside your app) and **server** (over TCP).
A built-in **Web UI** lets you browse and edit data from any browser.

---

## Table of Contents

1. [Concepts](#1-concepts)
2. [Project Structure](#2-project-structure)
3. [Getting Started](#3-getting-started)
4. [SQL Reference](#4-sql-reference)
   - 4.1 [Data Types](#41-data-types)
   - 4.2 [CREATE TABLE](#42-create-table)
   - 4.3 [DROP TABLE](#43-drop-table)
   - 4.4 [INSERT](#44-insert)
   - 4.5 [SELECT](#45-select)
   - 4.6 [UPDATE](#46-update)
   - 4.7 [DELETE](#47-delete)
   - 4.8 [Expressions and Operators](#48-expressions-and-operators)
   - 4.9 [Aggregate Functions](#49-aggregate-functions)
   - 4.10 [JOINs](#410-joins)
5. [Embedded Mode](#5-embedded-mode)
6. [Server Mode](#6-server-mode)
7. [Client Library](#7-client-library)
8. [Web UI](#8-web-ui)
9. [CLI Reference](#9-cli-reference)
10. [Architecture Deep Dive](#10-architecture-deep-dive)
11. [Storage and Persistence](#11-storage-and-persistence)
12. [Performance Notes](#12-performance-notes)

---

## 1. Concepts

Before diving into SQL, here are the core ideas behind LambdaSQL.

### Database
A **database** in LambdaSQL is a directory on disk (or an in-memory context).
It holds a set of **tables** and a **catalog** that describes their schemas.
There is no concept of multiple databases per instance — one engine instance = one database.

### Table
A **table** is a named collection of rows that all share the same structure (schema).
Each table has one or more **columns**, each with a fixed data type.

### Row
A **row** (also called a record) is a single entry in a table.
Every row has a value for each column defined in the table's schema.

### Column
A **column** defines one field of a table. It has:
- a **name** (case-insensitive)
- a **data type** (`int`, `bigint`, `float`, `text`, `bool`)
- optional constraints: `not null`, `primary key`, `default`

### Primary Key
A **primary key** is a column whose value uniquely identifies each row.
LambdaSQL maintains an in-memory hash index on the primary key for O(1) lookups.
Only one primary key per table is supported. A primary key column is implicitly `not null`.

### Schema
The **schema** is the complete definition of a table: its name, columns, types, and constraints.
Schemas are persisted to `catalog.json` inside the data directory.

### Query
A **query** is a SQL statement sent to the engine. LambdaSQL parses it into an
Abstract Syntax Tree (AST), plans it, and executes it against the stored data.

### Supported Statements

| Statement | Purpose |
|-----------|---------|
| `CREATE TABLE` | Define a new table |
| `DROP TABLE` | Remove a table and all its data |
| `INSERT` | Add one or more rows |
| `SELECT` | Read and filter rows |
| `UPDATE` | Modify existing rows |
| `DELETE` | Remove rows |

### Case Sensitivity
SQL keywords and column/table names are **case-insensitive**.
`SELECT`, `select`, and `Select` are all identical.
String values inside quotes are case-sensitive.

### NULL
`NULL` represents the absence of a value. Any column without `not null` can hold `NULL`.
Arithmetic or comparison with `NULL` returns `NULL` (or `false` in boolean context).

---

## 2. Project Structure

LambdaSQL.sln  
├── LambdaSQL.Core  
│   Core engine: lexer, parser, executor, storage  
│   ├── Lexer/  
│   │   Tokenizer — breaks SQL text into tokens  
│   ├── Parser/  
│   │   Recursive-descent parser — builds AST  
│   │   └── Ast/  
│   │       AST node types (Statements, Expressions)  
│   ├── Executor/  
│   │   Query executor + expression evaluator  
│   ├── Storage/  
│   │   Table, Row, Column, DataType, page storage  
│   │   └── PagedStorage/  
│   │       8 KB page files, WAL, row serializer  
│   ├── Catalog/  
│   │   DatabaseCatalog — manages table registry  
│   └── Engine/  
│       DatabaseEngine — public API entry point  
│  
├── LambdaSQL.Server  
│   Standalone TCP server  
│   └── Protocol/  
│       Binary frame protocol (reader + writer)  
│  
├── LambdaSQL.Client  
│   .NET client library for remote connections  
│  
├── LambdaSQL.Cli  
│   Interactive command-line REPL  
│  
└── LambdaSQL.Web  
    ASP.NET Core web application + browser UI  
    └── wwwroot/  
        Static HTML/CSS/JS (no framework)

---

## 3. Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Windows 10+ or any modern Linux distribution

### Build everything

```bash
dotnet build LambdaSQL.sln -c Release
```

### Run the CLI (embedded, in-memory)

```bash
dotnet run --project LambdaSQL.Cli -c Release
```

You will see:

```
╔══════════════════════════════╗
║       LambdaSQL CLI          ║
╚══════════════════════════════╝
Mode: embedded  [in-memory]
Type SQL or 'help' / 'exit'

sql>
```

Type any SQL and press Enter.  
Type `tables` to list all tables.  
Type `exit` to quit.

---

### Run with persistent storage

```bash
dotnet run --project LambdaSQL.Cli -c Release -- --data ./mydb
```

Data is saved to `./mydb`. On the next start with the same `--data` path, all tables and rows are automatically restored.

---

## 4. SQL Reference

### 4.1 Data Types

| Type   | Description              | Notes                                  |
|--------|--------------------------|------------------------------------------|
| int    | 32-bit signed integer   | −2 147 483 648 to 2 147 483 647          |
| bigint | 64-bit signed integer   | −9.2 × 10¹⁸ to 9.2 × 10¹⁸                |
| float  | 64-bit floating point   | IEEE 754 double precision                |
| text   | UTF-8 string            | Arbitrary length                         |
| bool   | Boolean                 | true or false                            |

All types accept `NULL` unless the column is declared `NOT NULL`.

---

### 4.2 CREATE TABLE

Defines a new table with its columns and constraints.

#### Syntax

```sql
create table <table_name> (
    <column_name> <type> [primary key] [not null] [default <value>],
    ...
)
```

#### Examples

```sql
-- Simple table
create table products (
    id    int  primary key,
    name  text not null,
    price float
)
```

```sql
-- Table with defaults
create table orders (
    id         bigint primary key,
    product_id int    not null,
    quantity   int    not null default 1,
    note       text,
    active     bool   not null default true
)
```

#### Rules

- Table names must be unique within the database
- Column names must be unique within a table
- Only one primary key per table
- `default` values must be literals: numbers, strings, `true`, `false`, or `null`

---

### 4.3 DROP TABLE

Removes a table and all its data permanently.

```sql
drop table products;
drop table if exists temp_data;
```

`if exists` suppresses the error if the table does not exist.

---

### 4.4 INSERT

Adds one or more rows to a table.

```sql
-- Single row, named columns
insert into products (id, name, price) values (1, 'Laptop', 999.99);
```

```sql
-- Multiple rows at once
insert into products (id, name, price) values
    (2, 'Mouse',    29.99),
    (3, 'Keyboard', 59.99),
    (4, 'Monitor',  349.00);
```

```sql
-- Without column list
insert into products values (5, 'Webcam', 79.99);
```

```sql
-- NULL value
insert into products (id, name, price) values (6, 'Cable', null);
```

String values must be wrapped in single quotes `'...'`.  
Escape a single quote by doubling it: `'O''Brien'`.
```

---

### 4.5 SELECT

Reads rows from one or more tables.

#### Full syntax

```sql
select [distinct] <columns>
from <table> [as <alias>]
[inner join | left join <table> [as <alias>] on <condition>]
[where <condition>]
[group by <columns>]
[having <condition>]
[order by <column> [asc | desc], ...]
[limit <n>]
```

#### Examples

```sql
-- All columns
select * from products;
```

```sql
-- Specific columns
select name, price from products;
```

```sql
-- Column alias
select name as product_name, price * 1.2 as price_with_tax from products;
```

```sql
-- Filter
select * from products where price > 100;
select * from products where price >= 50 and price <= 200;
select * from products where name = 'Laptop';
```

```sql
-- Distinct values
select distinct city from users;
```

```sql
-- Sort
select * from products order by price desc;
select * from products order by name asc, price desc;
```

```sql
-- Limit
select * from products order by price desc limit 5;
```

```sql
-- NULL checks
select * from products where price is null;
select * from products where price is not null;
```

```sql
-- IN
select * from products where id in (1, 2, 3);
select * from products where name not in ('Cable', 'Mouse');
```

```sql
-- LIKE
select * from products where name like 'Key%';
select * from products where name not like '%cable%';
```

```sql
-- Arithmetic
select name, price * 0.9 as discounted from products;
```

---

### 4.6 UPDATE

Modifies existing rows.

```sql
-- Update one row
update products set price = 899.99 where id = 1;
```

```sql
-- Update multiple columns
update products set price = 24.99, name = 'Gaming Mouse' where id = 2;
```

```sql
-- Update with expression
update orders set quantity = quantity + 1 where product_id = 3;
```

```sql
-- Update all rows
update products set price = price * 1.05;
```

⚠️ Omitting `WHERE` updates every row in the table.

---

### 4.7 DELETE

Removes rows from a table.

```sql
-- Delete one row
delete from products where id = 6;
```

```sql
-- Delete by condition
delete from products where price < 30;
```

```sql
-- Delete all rows
delete from products;
```

⚠️ Omitting `WHERE` deletes every row in the table.

---

### 4.8 Expressions and Operators

Expressions can appear in `SELECT`, `WHERE`, `SET`, `ON`, `HAVING`, and `ORDER BY`.

#### Comparison

| Operator | Meaning |
|----------|--------|
| =        | Equal |
| !=, <>   | Not equal |
| <        | Less than |
| <=       | Less than or equal |
| >        | Greater than |
| >=       | Greater than or equal |

#### Logical

| Operator | Meaning |
|----------|--------|
| and      | Both conditions true |
| or       | At least one condition true |
| not      | Negates a condition |

Precedence (highest → lowest): `not → and → or`

```sql
where age > 18 and (city = 'Moscow' or city = 'London');
where not active;
```

#### Arithmetic

| Operator | Meaning |
|----------|--------|
| +        | Addition (or string concatenation) |
| -        | Subtraction |
| *        | Multiplication |
| /        | Division |
| %        | Modulo |

```sql
select price * quantity as total from order_items;
select name + ' (' + city + ')' as label from users;
```

#### Special predicates

```sql
where email is null;
where email is not null;
where id in (1, 2, 3);
where status not in ('deleted', 'banned');
where name like 'A%';
where code not like '%-test';
```

---

### 4.9 Aggregate Functions

| Function   | Description |
|------------|-------------|
| count(*)   | Number of rows |
| count(col) | Number of non-NULL values |
| sum(col)   | Sum of values |
| avg(col)   | Average |
| min(col)   | Minimum |
| max(col)   | Maximum |

```sql
select count(*) as total from products;
select avg(price) as avg_price from products;
select min(price) as cheapest, max(price) as most_expensive from products;
```

#### GROUP BY

```sql
select category, count(*) as cnt, avg(price) as avg_price
from products
group by category
order by cnt desc;
```

#### HAVING

```sql
select city, count(*) as users
from customers
group by city
having count(*) > 5
order by users desc;
```

**Rules:**
- Non-aggregated columns must appear in `GROUP BY`
- `WHERE` runs before grouping; `HAVING` after

---

### 4.10 JOINs

#### INNER JOIN

Returns only matching rows.

```sql
select o.id, u.name, o.total
from orders as o
inner join users as u on o.user_id = u.id;
```

#### LEFT JOIN

Returns all rows from the left table.

```sql
select u.name, o.id as order_id
from users as u
left join orders as o on u.id = o.user_id;
```

#### Chained JOINs

```sql
select u.name, p.name as product, oi.quantity
from users as u
inner join orders as o       on u.id = o.user_id
inner join order_items as oi on o.id = oi.order_id
inner join products as p     on oi.product_id = p.id;
```

Table aliases are optional but recommended.
```

---

## 5. Embedded Mode

Use LambdaSQL directly inside your C# application — no server, no network.

### Add a reference

```xml
<ProjectReference Include="..\LambdaSQL.Core\LambdaSQL.Core.csproj" />
```

---

### In-memory database

```csharp
using LambdaSQL.Core.Engine;

using var db = new DatabaseEngine();

db.Execute("create table users (id int primary key, name text not null, age int)");
db.Execute("insert into users values (1, 'Alice', 30)");
db.Execute("insert into users values (2, 'Bob', 25)");

var result = db.Execute("select * from users where age > 20 order by name");
result.Print();
```

---

### Persistent database

```csharp
using var db = new DatabaseEngine("./data");

// Tables and rows are loaded automatically on startup
db.Execute("insert into users values (3, 'Carol', 35)");
```

Changes are written to disk immediately.

---

### Execute multiple statements

```csharp
var results = db.ExecuteAll(@"
    insert into users values (4, 'Dave', 28);
    insert into users values (5, 'Eve', 22);
    select count(*) as total from users;
");

foreach (var r in results)
    r.Print();
```

---

### Working with QueryResult

```csharp
var result = db.Execute("select id, name, age from users");

// Column names
string[] cols = result.Columns;

// Rows
foreach (var row in result.Rows)
{
    long   id   = (long)row[0]!;
    string name = (string)row[1]!;
    int?   age  = row[2] is null ? null : Convert.ToInt32(row[2]);

    Console.WriteLine($"{id}: {name}, age {age}");
}
```

---

### DML result

```csharp
var ins = db.Execute("insert into users values (6, 'Frank', 40)");

Console.WriteLine(ins.RowsAffected); // 1
Console.WriteLine(ins.Message);      // "1 row(s) inserted."
```

---

### Thread safety

- `SELECT` queries run concurrently
- `INSERT`, `UPDATE`, `DELETE`, and DDL are exclusive

```csharp
var db = new DatabaseEngine("./data");

Parallel.For(0, 100, i =>
    db.Execute($"insert into events (id, name) values ({i}, 'event-{i}')"));
```

---

## 6. Server Mode

Run LambdaSQL as a standalone TCP server.

### Start the server

```bash
# Defaults: port 5464, data in ./data
dotnet run --project LambdaSQL.Server -c Release
```

```bash
# Custom options
dotnet run --project LambdaSQL.Server -c Release -- \
    --host 0.0.0.0 \
    --port 5464 \
    --data /var/lambdasql/data \
    --maxconn 256
```

---

### Options

| Flag      | Default | Description |
|----------|--------|-------------|
| --host   | 0.0.0.0 | IP address to bind |
| --port   | 5464   | TCP port |
| --data   | ./data | Data directory |
| --maxconn| 256    | Maximum concurrent connections |

Press `Ctrl+C` or send `SIGTERM` for graceful shutdown.

---

### Wire protocol

```
Frame layout:
  [4 bytes]  payload length (big-endian int32)
  [1 byte]   frame type
  [N bytes]  payload
```

#### Client → Server

```
0x01  Query   UTF-8 SQL string
0x02  Ping    (no payload)
```

#### Server → Client

```
0x10  Ok      serialized ResultSet
0x11  Error   UTF-8 error message
0x12  Pong    (no payload)
```

The protocol is language-agnostic — implement a client in any language with TCP sockets.

---

## 7. Client Library

`LambdaSQL.Client` connects to a running LambdaSQL server from .NET code.

### Add a reference

```xml
<ProjectReference Include="..\LambdaSQL.Client\LambdaSQL.Client.csproj" />
```

---

### Usage

```csharp
using LambdaSQL.Client;

await using var client = new LambdaSqlClient("localhost", 5464);
await client.ConnectAsync();

var result = await client.QueryAsync("select * from users");
result.Print();

if (result.IsError)
    Console.WriteLine("Error: " + result.ErrorMessage);

var ins = await client.QueryAsync("insert into users values (10, 'Grace', 27)");
Console.WriteLine(ins.RowsAffected); // 1

bool alive = await client.PingAsync();
```

---

### ClientResult properties

| Property        | Type         | Description |
|----------------|--------------|-------------|
| IsError        | bool         | True if error |
| ErrorMessage   | string?      | Error text |
| IsResultSet    | bool         | SELECT result |
| Columns        | string[]     | Column names |
| Rows           | object?[][]  | Row data |
| RowsAffected   | int          | Rows changed |
| Message        | string?      | Status text |

---

### Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

var result = await client.QueryAsync(
    "select * from large_table",
    cts.Token
);
```

---

## 8. Web UI

### Start

```bash
# In-memory
dotnet run --project LambdaSQL.Web -c Release
```

```bash
# Persistent
dotnet run --project LambdaSQL.Web -c Release -- --data=./mydb
```

Open http://localhost:5000 in your browser.

---

### Features

#### SQL Editor
- Write any SQL in the editor panel
- Press `Ctrl+Enter` or click ▶ Run
- Results appear in a formatted table
- Execution time is displayed
- Export CSV downloads results

#### Table Browser
- All tables listed in sidebar
- Click a table to open data view
- ↻ Refresh reloads tables

#### Data View
- Shows all rows
- Filter box narrows results instantly
- Click row → edit dialog
- `+ Insert Row` opens form

#### Create Table
- Click `+ New Table`
- Define columns visually (name, type, PK, NOT NULL)

#### Edit / Delete Row
- Edit → runs `UPDATE`
- Delete → runs `DELETE`

#### Drop Table
- Open table → click Drop Table → confirm

---

### REST API

#### POST /api/query

```json
{ "sql": "select * from users" }
```

Response:

```json
[{
  "success": true,
  "columns": ["id", "name", "age"],
  "rows": [{ "id": 1, "name": "Alice", "age": 30 }],
  "rowsAffected": 1
}]
```

#### GET /api/tables

```json
["users", "products", "orders"]
```

#### GET /api/tables/{name}

```json
{ "name": "users", "columns": [...] }
```

---

## 9. CLI Reference

### Launch options

```bash
# Embedded, in-memory
dotnet run --project LambdaSQL.Cli
```

```bash
# Embedded, persistent
dotnet run --project LambdaSQL.Cli -- --data ./mydb
```

```bash
# Remote server
dotnet run --project LambdaSQL.Cli -- --host localhost --port 5464
```

---

### REPL commands

| Command | Description |
|--------|-------------|
| tables | List tables |
| help   | Show help |
| ping   | Ping server |
| exit   | Exit |

---

### Multi-line input

```sql
sql> select *
  -> from users
  -> where age > 25;
```

---

### Timing

```
sql> select count(*) from users
+-------+
| count |
+-------+
| 42    |
+-------+
(1 row(s))
  (2ms)
```

---

## 10. Architecture Deep Dive

### Lexer

`Lexer.cs` converts SQL text into tokens.

Example:

```
select * from users where id = 1
```

Becomes:

```
[Select] [Star] [From] [Identifier 'users'] [Where]
[Identifier 'id'] [Equals] [Integer '1'] [Eof]
```

- Case-insensitive keywords
- Supports `'\'` escapes
- Skips `-- comments`

---

### Parser

`Parser.cs` builds an AST via recursive descent.

Precedence:

```
OR → AND → NOT → Comparison → Add/Sub → Mul/Div → Unary → Primary
```

Outputs strongly-typed C# records (`Statements.cs`, `Expressions.cs`).

---

### Executor

`Executor.cs` walks the AST.

#### SELECT pipeline:

1. FROM
2. WHERE
3. GROUP BY
4. HAVING
5. PROJECT
6. DISTINCT
7. ORDER BY
8. LIMIT

`ExprEvaluator` handles:
- arithmetic
- comparisons
- LIKE / IN / NULL
- aggregates

---

### Catalog

`DatabaseCatalog` manages tables and persists schema to `catalog.json`.

---

## 11. Storage and Persistence

### In-memory mode

- Uses in-memory lists
- No files created
- Data lost on exit

---

### Persistent mode

- Each table → `<name>.tbl`
- Schema → `catalog.json`

---

### Page format (8 KB)

- Header (12 bytes): magic, page ID, slot count, free offset
- Slot directory (4 bytes per slot)
- Row data packed from end

Supports variable-length rows and fast deletion.

---

### Row serialization

```
2 bytes: column count
Per column:
  1 byte type tag + payload
```

Types:
- null → tag only
- int32 → 4 bytes
- int64 → 8 bytes
- float64 → 8 bytes
- text → length + UTF-8
- bool → 1 byte

---

### Write-Ahead Log (WAL)

- Changes written to `wal.log` before data files
- Ensures crash recovery
- Replayed on startup

---

### Primary key index

- `Dictionary<object, int>`
- O(1) lookups
- Avoids full scans

---

## 12. Performance Notes

- Reads are concurrent (`ReaderWriterLockSlim`)
- Writes are exclusive
- Full scans used without PK filter
- WAL uses buffered I/O (no fsync per write)
- Checkpoint on shutdown
- Page cache fully in memory
- Batch inserts are faster than single inserts
```

Please, someone hire me.
