#!/usr/bin/env python3
"""
建立 UI 測試用的 SQLite 資料庫 (只用 Python 標準函式庫)。

用法：python3 tools/make-test-db.py <輸出路徑.db>

內容 (同一天執行結果完全相同，日期以「今天」往回推 400 天)：
- StockDailyPrice：2330、0050 的日線 (固定亂數種子)
- StockList      ：模擬使用者的「上市公司基本資料」表 (中文欄位)，含 1101 台泥、1102 亞泥、2330 台積電
- StockNameList  ：0050 元大台灣50 (模擬「更新股票清單」同步過)
"""
import datetime
import os
import random
import sqlite3
import sys

if len(sys.argv) != 2:
    sys.exit(__doc__)

path = sys.argv[1]
if os.path.exists(path):
    os.remove(path)

db = sqlite3.connect(path)
db.executescript("""
CREATE TABLE StockDailyPrice (
    StockId TEXT NOT NULL, TradeDate TEXT NOT NULL, Volume INTEGER, Amount INTEGER,
    OpenPrice REAL, HighPrice REAL, LowPrice REAL, ClosePrice REAL,
    PriceChange REAL, TradeCount INTEGER, Note TEXT, PRIMARY KEY (StockId, TradeDate));
CREATE TABLE StockNameList (
    StockId TEXT NOT NULL PRIMARY KEY, StockName TEXT NOT NULL,
    Market TEXT NOT NULL DEFAULT 'TWSE', UpdatedAt TEXT NOT NULL);
CREATE TABLE StockList ("出表日期" TEXT, "公司代號" TEXT, "公司名稱" TEXT, "公司簡稱" TEXT, "產業別" TEXT);
""")

rng = random.Random(42)
start = datetime.date.today() - datetime.timedelta(days=400)
prices = {"2330": 900.0, "0050": 150.0}
for offset in range(401):
    day = start + datetime.timedelta(days=offset)
    if day.weekday() > 4:
        continue
    for stock_id in prices:
        prev = prices[stock_id]
        close = round(prev * (1 + rng.uniform(-0.02, 0.02)), 2)
        prices[stock_id] = close
        db.execute("INSERT INTO StockDailyPrice VALUES (?,?,?,?,?,?,?,?,?,?,NULL)",
                   (stock_id, day.isoformat(), 1000, 100000, prev, max(prev, close) * 1.005,
                    min(prev, close) * 0.995, close, round(close - prev, 2), 10))

db.executemany("INSERT INTO StockList VALUES (?,?,?,?,?)", [
    ("1150120", "1101", "臺灣水泥股份有限公司", "台泥", "01"),
    ("1150120", "1102", "亞洲水泥股份有限公司", "亞泥", "01"),
    ("1150120", "2330", "台灣積體電路製造股份有限公司", "台積電", "24"),
])
db.execute("INSERT INTO StockNameList VALUES ('0050', '元大台灣50', 'TWSE', '2026-01-01 00:00:00')")
db.commit()
print(f"已建立 {path}")
