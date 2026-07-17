window.arrdashDrag = {
    attachGhost: function (handleEl) {
        if (!handleEl || handleEl.__arrdashGhostAttached) return;
        handleEl.__arrdashGhostAttached = true;

        handleEl.addEventListener("dragstart", function (e) {
            const target = handleEl.closest(".dashboard-panel-wrap, .activity-layout-item");
            if (!target || !e.dataTransfer) return;

            const rect = target.getBoundingClientRect();
            e.dataTransfer.setDragImage(target, e.clientX - rect.left, e.clientY - rect.top);
        });
    },

    placementFromX: function (gridRect, clientX) {
        const width = Math.max(gridRect.width, 1);
        const relX = (clientX - gridRect.left) / width;
        if (relX < 0.40) return "left";
        if (relX <= 0.60) return "full";
        return "right";
    },

    clusterRows: function (items) {
        const sorted = items.slice().sort(function (a, b) {
            if (Math.abs(a.rect.top - b.rect.top) > 8) return a.rect.top - b.rect.top;
            return a.rect.left - b.rect.left;
        });

        const rows = [];
        for (const item of sorted) {
            let row = null;
            for (const candidate of rows) {
                if (Math.abs(candidate.top - item.rect.top) < 32) {
                    row = candidate;
                    break;
                }
            }
            if (!row) {
                row = { top: item.rect.top, bottom: item.rect.bottom, items: [] };
                rows.push(row);
            }
            row.items.push(item);
            row.top = Math.min(row.top, item.rect.top);
            row.bottom = Math.max(row.bottom, item.rect.bottom);
        }

        rows.sort(function (a, b) { return a.top - b.top; });
        for (const row of rows) {
            row.items.sort(function (a, b) { return a.rect.left - b.rect.left; });
        }
        return rows;
    },

    indexInDomOrder: function (items, id) {
        for (let i = 0; i < items.length; i++) {
            if (items[i].id === id) return i;
        }
        return -1;
    },

    findRowAtY: function (rows, clientY, gridRect) {
        if (!rows.length) return null;

        if (clientY < rows[0].top) return rows[0];

        for (let i = 0; i < rows.length; i++) {
            const row = rows[i];
            const next = rows[i + 1];
            const bandEnd = next ? (row.bottom + next.top) / 2 : gridRect.bottom + 1000;
            if (clientY < bandEnd) return row;
        }

        return rows[rows.length - 1];
    },

    isFullWidthItem: function (item, gridRect) {
        return item.isFullWidth || item.rect.width > gridRect.width * 0.55;
    },

    resolveRowDrop: function (items, rowItems, placement, draggedId, gridRect) {
        if (!rowItems.length) return null;

        if (rowItems.length === 1) {
            const item = rowItems[0];
            if (!this.isFullWidthItem(item, gridRect)) return null;

            const insertIndex = this.indexInDomOrder(items, item.id);
            if (draggedId === item.id) {
                return { action: "insert", placement: placement, insertIndex: insertIndex, targetId: null };
            }

            if (placement === "left" || placement === "right") {
                return { action: "splitFull", placement: placement, insertIndex: insertIndex, targetId: item.id };
            }

            if (placement === "full") {
                return { action: "replaceFull", placement: placement, insertIndex: insertIndex, targetId: item.id };
            }
        }

        if (rowItems.length === 2) {
            const leftItem = rowItems[0];
            const rightItem = rowItems[1];
            const insertIndex = this.indexInDomOrder(items, leftItem.id);
            const draggedInRow = draggedId === leftItem.id || draggedId === rightItem.id;

            if (!draggedInRow) return null;

            if (placement === "full") {
                return { action: "expandToFull", placement: placement, insertIndex: insertIndex, targetId: leftItem.id };
            }

            if (placement === "left" && draggedId === rightItem.id) {
                return { action: "swapPair", placement: placement, insertIndex: insertIndex, targetId: leftItem.id };
            }

            if (placement === "right" && draggedId === leftItem.id) {
                return { action: "swapPair", placement: placement, insertIndex: insertIndex, targetId: leftItem.id };
            }
        }

        return null;
    },

    resolveActivityDrop: function (gridEl, clientX, clientY, draggedId) {
        if (!gridEl) return null;

        const gridRect = gridEl.getBoundingClientRect();
        const placement = this.placementFromX(gridRect, clientX);

        const items = Array.from(gridEl.querySelectorAll(".activity-layout-item[data-section-id]"))
            .map(function (el) {
                return {
                    id: el.getAttribute("data-section-id"),
                    rect: el.getBoundingClientRect(),
                    isFullWidth: el.classList.contains("span-full")
                };
            });

        if (!items.length) {
            return { action: "insert", insertIndex: 0, placement: placement, targetId: null };
        }

        const rows = this.clusterRows(items);
        const targetRow = this.findRowAtY(rows, clientY, gridRect);

        if (targetRow) {
            const rowDrop = this.resolveRowDrop(items, targetRow.items, placement, draggedId, gridRect);
            if (rowDrop) return rowDrop;
        }

        if (clientY < rows[0].top) {
            return { action: "insert", insertIndex: 0, placement: placement, targetId: null };
        }

        if (clientY > rows[rows.length - 1].bottom) {
            return { action: "insert", insertIndex: items.length, placement: placement, targetId: null };
        }

        const rowItems = targetRow ? targetRow.items : rows[0].items;
        let insertIndex = items.length;

        if (placement === "full" || placement === "left") {
            insertIndex = this.indexInDomOrder(items, rowItems[0].id);
        } else {
            const soleItem = rowItems.length === 1;
            const wideItem = soleItem && this.isFullWidthItem(rowItems[0], gridRect);
            if (wideItem) {
                insertIndex = this.indexInDomOrder(items, rowItems[0].id) + 1;
            } else if (rowItems.length >= 2) {
                insertIndex = this.indexInDomOrder(items, rowItems[1].id);
            } else {
                insertIndex = this.indexInDomOrder(items, rowItems[0].id) + 1;
            }
        }

        if (insertIndex < 0) insertIndex = items.length;

        return { action: "insert", insertIndex: insertIndex, placement: placement, targetId: null };
    }
};
