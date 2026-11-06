// sub-order-drag.js：附圖勾選項拖曳排序的輔助程式 (全站載入一次，事件掛在 document)
// - 允許放開：dragover 取消預設行為
// - Firefox 要求 dragstart 時必須 setData，否則拖不動 (Blazor 的 @ondragstart 在伺服器端處理，無法設定)
// - 拖曳中 / 經過目標時的樣式 (.dragging / .drag-over) 也在這裡加減：不經過伺服器，拖曳才順暢
// 真正的重排由 ChartDisplayOptions.razor 的 @ondrop 處理。只認有 data-sub-drag 屬性的元素。
(function () {
    const itemOf = e => (e.target instanceof Element ? e.target.closest('[data-sub-drag]') : null);
    const clearOver = () => document.querySelectorAll('.drag-over').forEach(el => el.classList.remove('drag-over'));

    document.addEventListener('dragstart', e => {
        const el = itemOf(e);
        if (!el) return;
        e.dataTransfer.setData('text/plain', el.dataset.subDrag);
        e.dataTransfer.effectAllowed = 'move';
        el.classList.add('dragging');
    });
    // 放開要被允許，必須在 dragover 取消預設行為 (dragover 連續觸發，所以不交給 Blazor 送到伺服器)
    document.addEventListener('dragover', e => {
        if (itemOf(e)) e.preventDefault();
    });
    document.addEventListener('dragenter', e => {
        const el = itemOf(e);
        if (!el) return;
        clearOver();
        el.classList.add('drag-over');
    });
    document.addEventListener('dragend', () => {
        clearOver();
        document.querySelectorAll('.dragging').forEach(el => el.classList.remove('dragging'));
    });
    document.addEventListener('drop', () => {
        clearOver();
        document.querySelectorAll('.dragging').forEach(el => el.classList.remove('dragging'));
    });
})();
