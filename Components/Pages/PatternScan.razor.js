// PatternScan.razor.js：繪製型態掃描的小卡與參數旁的型態範例
// 小卡 = K 線區 (固定高度) + 0 ~ 多個附圖 (成交量 / MACD / KD / RSI / 三大法人 / 外資 / 投信 / 自營商) 依序疊在下方
// 附圖定義與組圖程式在 wwwroot/js/candle-chart.js (與技術圖表頁共用)

// 版本參數沿用本檔網址的 ?v=，讓共用檔也跟著更新 (避免瀏覽器沿用快取中的舊版)
const { UP, DOWN, AXIS_COLOR, MA_LINES, SUB_CHARTS, buildStackedOption, stackedHeight } = await import('../../js/candle-chart.js' + new URL(import.meta.url).search);

const SIGNAL_COLOR = '#ffd54f';   // 型態標示
const HORIZON_COLOR = '#8392A5';  // 觀察日標示

// 小卡版面 (像素)：K 線區高度固定，每多一個附圖，小卡就增高一列
const LAYOUT = {
    top: 36,          // 上方留給均線圖例與觀察日標籤
    candleHeight: 150,
    subLegend: 18,    // 附圖上方的圖例列
    subHeight: 48,
    bottom: 26,       // 最下方的日期標籤
    left: 48,
    right: 12
};

// 小卡高度：K 線區 + 每個附圖列 (費半列加高，見 candle-chart.js 的 heightScale)
export function cardHeight(subs) {
    return stackedHeight(LAYOUT, subs);
}

let cardCharts = [];
let previewChart = null;

// 離開頁面時釋放所有圖表
export function disposeAll() {
    cardCharts.forEach(c => c.dispose());
    cardCharts = [];
    if (previewChart) { previewChart.dispose(); previewChart = null; }
}

// 換頁或重新掃描時，先釋放上一批小卡圖表，避免記憶體累積
// visibleMas：預設顯示的均線名稱 (例如 ['MA5','MA20'])；其他均線仍在圖例中 (灰色)，點一下可單張顯示
// horizons：觀察天數 (例如 [5, 10, 20, 60])，在型態成立日之後第 N 根 K 棒畫淡色直線
// subCharts：要疊在 K 線下方的附圖，依序排列，例如 ['volume', 'macd']
// settings：指標參數 (RSI 的圖例名稱會用到)
export function renderPatternCards(cards, visibleMas, horizons, subCharts, settings) {
    cardCharts.forEach(c => c.dispose());
    cardCharts = [];
    const subs = (subCharts || []).filter(s => SUB_CHARTS[s]);
    const selected = Object.fromEntries(MA_LINES.map(m => [m.name, (visibleMas || []).includes(m.name)]));
    for (const card of cards) {
        const dom = document.getElementById(card.containerId);
        if (!dom) continue;
        dom.style.height = cardHeight(subs) + 'px';   // 先設定高度再建立圖表
        const chart = echarts.init(dom);
        const option = buildCardOption(card.candles, card.signalIndex, card.barCount, horizons || [], subs, settings || {});
        option.legend[0].selected = selected;
        chart.setOption(option);
        cardCharts.push(chart);
    }
}

// 型態範例：參數改變時重畫 (沿用同一個圖表實例)；固定只畫 K 線 + 成交量
export function renderPreview(containerId, candles, signalIndex, barCount) {
    const dom = document.getElementById(containerId);
    if (!dom) return;
    if (!previewChart || previewChart.getDom() !== dom) {
        if (previewChart) previewChart.dispose();
        previewChart = echarts.init(dom);
    }
    previewChart.setOption(buildPreviewOption(candles, signalIndex, barCount), true);
}

// ── K 線 series (小卡與預覽共用) ──
function candleSeries(candles, signalIndex, barCount, horizons) {
    const dates = candles.map(c => c.date);
    const signalDate = dates[signalIndex];
    const patternMarks = [];
    for (let i = Math.max(0, signalIndex - barCount + 1); i <= signalIndex; i++) {
        patternMarks.push({ coord: [dates[i], candles[i].low] });
    }
    return {
        type: 'candlestick',
        data: candles.map(c => [c.open, c.close, c.low, c.high]),
        itemStyle: { color: UP, color0: DOWN, borderColor: UP, borderColor0: DOWN },
        // 型態成立日：黃色虛線；型態每根 K 棒：下方三角形
        // (markArea 起訖為同一類別時寬度為 0 不會顯示，所以不用它)
        markLine: {
            silent: true, symbol: 'none', label: { show: false },
            lineStyle: { color: SIGNAL_COLOR, type: 'dashed', width: 1 },
            data: [
                { xAxis: signalDate },
                // 觀察日：型態成立後第 N 個交易日 (之後報酬就是算到這一天的收盤)；超出資料範圍的不畫
                ...horizons
                    .filter(h => signalIndex + h < dates.length)
                    .map(h => ({
                        xAxis: dates[signalIndex + h],
                        lineStyle: { color: HORIZON_COLOR, type: 'dotted', width: 1, opacity: 0.55 },
                        label: { show: true, formatter: `${h}日`, position: 'end', color: HORIZON_COLOR, fontSize: 9, distance: 2 }
                    }))
            ]
        },
        markPoint: {
            silent: true, symbol: 'triangle', symbolSize: 8, symbolOffset: [0, 11],
            label: { show: false }, itemStyle: { color: SIGNAL_COLOR },
            data: patternMarks
        }
    };
}


// ── 小卡 ──
function buildCardOption(candles, signalIndex, barCount, horizons, subs, settings) {
    const signalDate = candles[signalIndex]?.date;
    return buildStackedOption({
        candles, subs, settings, layout: LAYOUT,
        mainSeries: candleSeries(candles, signalIndex, barCount, horizons),
        tooltipTitle: c => `${c.date}${c.date === signalDate ? '（型態成立）' : ''}`,
        dateFormatter: d => d.slice(5),
        maLegend: { top: 0, right: 8 }
    });
}

// ── 型態範例 (預覽) ──
function buildPreviewOption(candles, signalIndex, barCount) {
    const dates = candles.map(c => c.date);
    const axisLine = { lineStyle: { color: AXIS_COLOR } };
    return {
        animation: false,
        grid: [{ left: 6, right: 6, top: 6, height: '68%' }, { left: 6, right: 6, top: '80%', height: '16%' }],
        xAxis: [
            { type: 'category', data: dates, axisLine, axisLabel: { show: false }, axisTick: { show: false } },
            { type: 'category', gridIndex: 1, data: dates, axisLine, axisLabel: { show: false }, axisTick: { show: false } }
        ],
        yAxis: [
            { scale: true, axisLine, axisLabel: { show: false }, splitLine: { show: false } },
            { gridIndex: 1, axisLabel: { show: false }, axisLine: { show: false }, splitLine: { show: false } }
        ],
        series: [
            candleSeries(candles, signalIndex, barCount, []),
            { type: 'bar', xAxisIndex: 1, yAxisIndex: 1, data: candles.map(SUB_CHARTS.volume.bars) }
        ]
    };
}
