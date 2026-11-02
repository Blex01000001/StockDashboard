#!/usr/bin/env python3
"""
模擬證交所 API 的本機假伺服器 (只用 Python 標準函式庫)，用於連不到 twse.com.tw 時測試同步功能。

用法：python3 tools/fake-twse.py [port，預設 9999]
接著在 appsettings.json 加入：
  "Twse": { "StockDayUrl":    "http://127.0.0.1:9999/exchangeReport/STOCK_DAY",
            "StockDayAllUrl": "http://127.0.0.1:9999/v1/exchangeReport/STOCK_DAY_ALL" }
（雲端沙盒有 HTTP proxy，啟動網站時要加環境變數 NO_PROXY=127.0.0.1,localhost）

STOCK_DAY 的回應依 stockNo 決定：
  2330 + date=20261001 → tests/StockDashboard.Tests/Fixtures/stock_day_2330_202610.json (證交所實際回應)
  BLOCK               → HTML 網頁 (模擬被封鎖)
  E403                → HTTP 403
  其他                → 「很抱歉，沒有符合條件的資料!」
STOCK_DAY_ALL → tests/StockDashboard.Tests/Fixtures/stock_day_all.json
每個請求都會印出 stockNo、date 與 User-Agent。
"""
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

        if url.path.endswith("STOCK_DAY_ALL"):
            return self.reply(fixture("stock_day_all.json"))
        if stock == "E403":
            return self.reply("", status=403, content_type="text/plain")
        if stock == "BLOCK":
            return self.reply("<html><head><title>系統忙碌</title></head><body>請稍後再試</body></html>", content_type="text/html")
        if stock == "2330" and date == "20261001":
            return self.reply(fixture("stock_day_2330_202610.json"))
        return self.reply(NO_DATA)

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
