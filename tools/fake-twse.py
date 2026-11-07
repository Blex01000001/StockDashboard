#!/usr/bin/env python3
"""
模擬證交所 API 的本機假伺服器 (只用 Python 標準函式庫)，用於連不到 twse.com.tw 時測試同步功能。

用法：python3 tools/fake-twse.py [port，預設 9999]
接著在 appsettings.json 加入：
  "Twse": { "StockDayUrl":    "http://127.0.0.1:9999/exchangeReport/STOCK_DAY",
            "StockDayAllUrl": "http://127.0.0.1:9999/v1/exchangeReport/STOCK_DAY_ALL",
            "T86Url":         "http://127.0.0.1:9999/fund/T86",
            "QfiisUrl":       "http://127.0.0.1:9999/fund/MI_QFIIS",
            "RequestDelayMs": "200" },
  "FinMind": { "DataUrl": "http://127.0.0.1:9999/api/v4/data", "RequestDelayMs": "200" }
（雲端沙盒有 HTTP proxy，啟動網站時要加環境變數 NO_PROXY=127.0.0.1,localhost）

STOCK_DAY 的回應依 stockNo 決定：
  2330 + date=20261001 → tests/StockDashboard.Tests/Fixtures/stock_day_2330_202610.json (證交所實際回應)
  BLOCK               → HTML 網頁 (模擬被封鎖)
  E403                → HTTP 403
  其他                → 「很抱歉，沒有符合條件的資料!」
STOCK_DAY_ALL → tests/StockDashboard.Tests/Fixtures/stock_day_all.json
T86 / MI_QFIIS (三大法人) 依 date 決定：
  週一～週五 → t86_20261030.json / mi_qfiis_20261030.json (證交所實際回應，日期欄改成請求的日期)
  週末       → t86_no_data.json (證交所實際的「沒有符合條件的資料」回應)
  20000101  → HTML 網頁 (模擬被封鎖)
FinMind /api/v4/data 依 dataset 與 data_id 決定：
  BAN   → {"msg":"ip banned","status":403} (模擬 IP 被封鎖)
  其他  → finmind_buysell_2330.json / finmind_shareholding_2330.json (使用者提供的實際回應，stock_id 改成請求的代號)
  dataset=TaiwanStockDividendResult → finmind_dividend_result_2330.json (除權除息結果表，stock_id 改成請求的代號)
oilpriceapi /v1/prices/past_year：
  標頭 Authorization: Token BAD → HTTP 401；沒帶 Token → HTTP 401；其他 → oilprice_past_year.json (使用者提供的實際回應，截成 12 天)
  設定：OilPrice:PastYearUrl = http://127.0.0.1:9999/v1/prices/past_year，OilPrice:ApiKey 隨便填 (填 BAD 可測失敗)
Yahoo Finance /v8/finance/chart/<symbol> (美股指數日線)：
  symbol 為 BAN → HTTP 429 (模擬請求過多)；其他 → yahoo_sox_chart.json (依 Yahoo 回應結構寫的合成資料，不管請求的區間)
  設定：MarketIndex:ChartUrl = http://127.0.0.1:9999/v8/finance/chart
每個請求都會印出 stockNo、date 與 User-Agent。
"""
import datetime
import http.server
import json
import os
import sys
import urllib.parse

FIXTURES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tests", "StockDashboard.Tests", "Fixtures")
NO_DATA = json.dumps({"stat": "很抱歉，沒有符合條件的資料!"}, ensure_ascii=False)


def fixture(name):
    with open(os.path.join(FIXTURES, name), encoding="utf-8") as f:
        return f.read()


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        url = urllib.parse.urlparse(self.path)
        query = urllib.parse.parse_qs(url.query)
        stock, date = query.get("stockNo", [""])[0], query.get("date", [""])[0]
        print(f"{url.path} stockNo={stock} date={date} UA={self.headers.get('User-Agent', '')[:40]}", flush=True)

        if "/v8/finance/chart/" in url.path:
            symbol = urllib.parse.unquote(url.path.rsplit("/", 1)[-1])
            print(f"yahoo chart {symbol} {query.get('period1', [''])[0]}~{query.get('period2', [''])[0]} interval={query.get('interval', [''])[0]}", flush=True)
            if symbol == "BAN":
                return self.reply("Too Many Requests", status=429, content_type="text/plain")
            return self.reply(fixture("yahoo_sox_chart.json"))
        if url.path.endswith("/v1/prices/past_year"):
            return self.oilprice()
        if url.path.endswith("/api/v4/data"):
            return self.finmind(query)
        if url.path.endswith("STOCK_DAY_ALL"):
            return self.reply(fixture("stock_day_all.json"))
        if url.path.endswith("T86") or url.path.endswith("MI_QFIIS"):
            return self.institutional(url.path, date)
        if stock == "E403":
            return self.reply("", status=403, content_type="text/plain")
        if stock == "BLOCK":
            return self.reply("<html><head><title>系統忙碌</title></head><body>請稍後再試</body></html>", content_type="text/html")
        if stock == "2330" and date == "20261001":
            return self.reply(fixture("stock_day_2330_202610.json"))
        return self.reply(NO_DATA)

    def oilprice(self):
        auth = self.headers.get("Authorization", "")
        print(f"oilpriceapi past_year auth={'Token ***' if auth.startswith('Token ') else 'none'}", flush=True)
        if not auth.startswith("Token ") or auth == "Token BAD":
            return self.reply(json.dumps({"status": "error", "message": "unauthorized"}), status=401)
        return self.reply(fixture("oilprice_past_year.json"))

    def finmind(self, query):
        dataset, stock_id = query.get("dataset", [""])[0], query.get("data_id", [""])[0]
        print(f"FinMind {dataset} {stock_id} {query.get('start_date', [''])[0]}~{query.get('end_date', [''])[0]}", flush=True)
        if stock_id == "BAN":
            return self.reply(fixture("finmind_ip_banned.json"), status=403)
        if dataset == "TaiwanStockDividendResult":
            name = "finmind_dividend_result_2330.json"
        else:
            name = "finmind_buysell_2330.json" if dataset == "TaiwanStockInstitutionalInvestorsBuySell" else "finmind_shareholding_2330.json"
        body = json.loads(fixture(name))
        for row in body["data"]:
            row["stock_id"] = stock_id
        return self.reply(json.dumps(body, ensure_ascii=False))

    def institutional(self, path, date):
        if date == "20000101":
            return self.reply("<html><body>系統忙碌</body></html>", content_type="text/html")
        try:
            day = datetime.datetime.strptime(date, "%Y%m%d")
        except ValueError:
            return self.reply(fixture("t86_no_data.json"))
        if day.weekday() > 4:
            return self.reply(fixture("t86_no_data.json"))
        body = json.loads(fixture("t86_20261030.json" if path.endswith("T86") else "mi_qfiis_20261030.json"))
        body["date"] = date
        return self.reply(json.dumps(body, ensure_ascii=False))

    def reply(self, body, status=200, content_type="application/json"):
        data = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", f"{content_type}; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *args):
        pass


port = int(sys.argv[1]) if len(sys.argv) > 1 else 9999
print(f"假證交所伺服器：http://127.0.0.1:{port}", flush=True)
http.server.HTTPServer(("127.0.0.1", port), Handler).serve_forever()
