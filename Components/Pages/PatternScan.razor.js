// PatternScan.razor.js：繪製型態掃描的小卡與參數旁的型態範例 (K 線 + 均線 + 成交量)

const UP = '#ef5350';     // 漲：紅 (台股配色)
const DOWN = '#26a69a';   // 跌：綠
const MA5_COLOR = '#f5c242';
const MA20_COLOR = '#42a5f5';
const SIGNAL_COLOR = '#ffd54f';   // 型態標示

let cardCharts = [];
let previewChart = null;

// 離開頁面時釋放所有圖表
export function disposeAll() {
    cardCharts.forEach(c => c.dispose());
    cardCharts = [];
    if (previewChart) { previewChart.dispose(); previewChart = null; }
}

// 換頁或重新掃描時，先釋放上一批小卡圖表，避免記憶體累積
export function renderPatternCards(cards) {
    cardCharts.forEach(c => c.dispose());
    cardCharts = [];
    for (const card of cards) {
        const dom = document.getElementById(card.containerId);
        if (!dom) continue;
        const chart = echarts.init(dom);
        chart.setOption(buildOption(card.candles, card.signalIndex, card.barCount, { showMa: true, showAxisLabels: true }));
        cardCharts.push(chart);
    }
}

// 型態範例：參數改變時重畫 (沿用同一個圖表實例)
export function renderPreview(containerId, candles, signalIndex, barCount) {
    const dom = document.getElementById(containerId);
    if (!dom) return;
    if (!previewChart || previewChart.getDom() !== dom) {
        if (previewChart) previewChart.dispose();
        previewChart = echarts.init(dom);
    }
    previewChart.setOption(buildOption(candles, signalIndex, barCount, { showMa: false, showAxisLabels: false }), true);
}

function buildOption(candles, signalIndex, barCount, { showMa, showAxisLabels }) {
    const dates = candles.map(c => c.date);
    const signalDate = dates[signalIndex];
    const axisLine = { lineStyle: { color: '#8392A5' } };
    const firstPatternBar = Math.max(0, signalIndex - barCount + 1);

    // 型態的每一根 K 棒下方放黃色三角形
    const patternMarks = [];
    for (let i = firstPatternBar; i <= signalIndex; i++) {
        patternMarks.push({ coord: [dates[i], candles[i].low] });
    }

    const series = [
        {
            type: 'candlestick',
            data: candles.map(c => [c.open, c.close, c.low, c.high]),
            itemStyle: { color: UP, color0: DOWN, borderColor: UP, borderColor0: DOWN },
            // 型態成立日：黃色虛線；型態每根 K 棒：下方三角形
            // (markArea 起訖為同一類別時寬度為 0 不會顯示，所以不用它)
            markLine: {
                silent: true, symbol: 'none', label: { show: false },
                lineStyle: { color: SIGNAL_COLOR, type: 'dashed', width: 1 },
                data: [{ xAxis: signalDate }]
            },
            markPoint: {
                silent: true, symbol: 'triangle', symbolSize: 8, symbolOffset: [0, 11],
                label: { show: false }, itemStyle: { color: SIGNAL_COLOR },
                data: patternMarks
            }
        },
        {
            type: 'bar', xAxisIndex: 1, yAxisIndex: 1,
            data: candles.map(c => ({ value: c.volume, itemStyle: { color: c.close >= c.open ? UP : DOWN } }))
        }
    ];
    if (showMa) {
        series.push(
            { name: 'MA5', type: 'line', data: candles.map(c => c.ma5), showSymbol: false, lineStyle: { width: 1, color: MA5_COLOR }, itemStyle: { color: MA5_COLOR } },
            { name: 'MA20', type: 'line', data: candles.map(c => c.ma20), showSymbol: false, lineStyle: { width: 1, color: MA20_COLOR }, itemStyle: { color: MA20_COLOR } }
        );
    }

    return {
        animation: false,
        legend: showMa
            ? { data: ['MA5', 'MA20'], top: 0, right: 8, itemWidth: 14, itemHeight: 8, textStyle: { color: '#8392A5', fontSize: 11 } }
            : undefined,
        tooltip: showAxisLabels ? {
            trigger: 'axis',
            axisPointer: { type: 'cross' },
            backgroundColor: 'rgba(30, 34, 45, 0.9)',
            borderColor: '#434651',
            textStyle: { color: '#d1d4dc', fontSize: 12 },
            formatter: params => {
                const c = candles[params[0].dataIndex];
                const color = c.close >= c.open ? UP : DOWN;
                return `<div style="font-weight:bold;color:#fff;">${c.date}${c.date === signalDate ? '（型態成立）' : ''}</div>
                    <div style="color:${color}">開 ${c.open}　高 ${c.high}<br/>低 ${c.low}　收 ${c.close}</div>
                    <div>量 ${c.volume.toLocaleString()}</div>
                    <div style="color:${MA5_COLOR}">MA5 ${c.ma5 ?? '-'}</div>
                    <div style="color:${MA20_COLOR}">MA20 ${c.ma20 ?? '-'}</div>`;
            }
        } : undefined,
        axisPointer: { link: [{ xAxisIndex: 'all' }] },
        grid: showAxisLabels
            ? [{ left: 48, right: 12, top: 22, height: '58%' }, { left: 48, right: 12, top: '76%', height: '14%' }]
            : [{ left: 6, right: 6, top: 6, height: '68%' }, { left: 6, right: 6, top: '80%', height: '16%' }],
        xAxis: [
            { type: 'category', data: dates, axisLine, axisLabel: { show: false }, axisTick: { show: false } },
            { type: 'category', gridIndex: 1, data: dates, axisLine, axisTick: { show: false },
              axisLabel: { show: showAxisLabels, color: '#8392A5', fontSize: 10, formatter: d => d.slice(5) } }
        ],
        yAxis: [
            { scale: true, axisLine, axisLabel: { show: showAxisLabels, color: '#8392A5', fontSize: 10 },
              splitLine: { show: showAxisLabels, lineStyle: { color: '#2d3139' } } },
            { gridIndex: 1, axisLabel: { show: false }, axisLine: { show: false }, splitLine: { show: false } }
        ],
        series
    };
}
