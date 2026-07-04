window.arrdashDrag = {
    attachGhost: function (handleEl) {
        if (!handleEl || handleEl.__arrdashGhostAttached) return;
        handleEl.__arrdashGhostAttached = true;

        handleEl.addEventListener("dragstart", function (e) {
            const target = handleEl.closest(".dashboard-panel-wrap");
            if (!target || !e.dataTransfer) return;

            const rect = target.getBoundingClientRect();
            e.dataTransfer.setDragImage(target, e.clientX - rect.left, e.clientY - rect.top);
        });
    }
};
