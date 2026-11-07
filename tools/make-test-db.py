#!/usr/bin/env python3
"""
建立 UI 測試用的 SQLite 資料庫 (只用 Python 標準函式庫)。

用法：python3 tools/make-test-db.py <輸出路徑.db>

內容 (同一天執行結果完全相同，日期以「今天」往回推 400 天)：
- StockDailyPrice：2330、0050 的日線 (固定亂數種子)
- StockList      ：模擬使用者的「上市公司基本資料」表 (中文欄位)，含 1101 台泥、1102 亞泥、2330 台積電
- StockNameList  ：0050 元大台灣50 (模擬「更新股票清單」同步過)
- InstitutionalDaily：只有 2330 有三大法人資料 (0050 沒有，用來檢查附圖的「無資料」提示)
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
CREATE TABLE InstitutionalDaily (
    StockId TEXT NOT NULL, TradeDate TEXT NOT NULL, ForeignNet INTEGER, ForeignDealerNet INTEGER,
    TrustNet INTEGER, DealerNet INTEGER, TotalNet INTEGER, IssuedShares INTEGER, ForeignShares INTEGER,
    ForeignHoldingPct REAL, PRIMARY KEY (StockId, TradeDate));
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
                   (stock_id, day.isoformat(), 1000, 100000, prev, round(max(prev, close) * 1.005, 2),
                    round(min(prev, close) * 0.995, 2), close, round(close - prev, 2), 10))

# 三大法人：另用獨立亂數種子，不影響上面的日線資料
inst_rng = random.Random(7)
issued = 25_930_380_458
holding = 18_500_000_000
for offset in range(401):
    day = start + datetime.timedelta(days=offset)
    if day.weekday() > 4:
        continue
    foreign = inst_rng.randint(-20_000, 20_000) * 1000
    foreign_dealer = inst_rng.randint(-50, 50) * 1000
    trust = inst_rng.randint(-3_000, 3_000) * 1000
    dealer = inst_rng.randint(-2_000, 2_000) * 1000
    holding += foreign + foreign_dealer
    db.execute("INSERT INTO InstitutionalDaily VALUES (?,?,?,?,?,?,?,?,?,?)",
               ("2330", day.isoformat(), foreign, foreign_dealer, trust, dealer,
                foreign + foreign_dealer + trust + dealer, issued, holding, round(holding * 100 / issued, 2)))

db.executemany("INSERT INTO StockList VALUES (?,?,?,?,?)", [
    ("1150120", "1101", "臺灣水泥股份有限公司", "台泥", "01"),
    ("1150120", "1102", "亞洲水泥股份有限公司", "亞泥", "01"),
    ("1150120", "2330", "台灣積體電路製造股份有限公司", "台積電", "24"),
])
db.execute("INSERT INTO StockNameList VALUES ('0050', '元大台灣50', 'TWSE', '2026-01-01 00:00:00')")
db.commit()
print(f"已建立 {path}")
