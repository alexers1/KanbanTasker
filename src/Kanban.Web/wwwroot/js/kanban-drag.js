// Column drag-to-reorder for the board, via SortableJS.
//
// Blazor natively speaks HTML5 drag events (used for cards), but that API can't do
// press-and-hold activation, touch dragging, or the "make room" reorder animation.
// SortableJS handles all three; this file is the thin bridge between it and Blazor.
//
// Desktop + tablet only: reordering is disabled at phone widths (the phone layout
// shows one column at a time via a tab bar, so there's nothing to drag).
window.kanbanDrag = (function () {
    var PHONE_MAX = 640;          // keep in sync with the ≤640px CSS breakpoint
    var CONTAINER_ID = 'columns-container';
    var resizeHooked = false;

    function phone() { return window.innerWidth <= PHONE_MAX; }

    return {
        // Attach (or re-attach) Sortable to the columns container. Safe to call repeatedly.
        initColumnSort: function (dotnetRef, delayMs, animationMs) {
            var el = document.getElementById(CONTAINER_ID);
            if (!el || typeof Sortable === 'undefined') return;

            if (el._kbSortable) { el._kbSortable.destroy(); el._kbSortable = null; }

            el._kbSortable = Sortable.create(el, {
                draggable: '.column',           // only real columns move…
                filter: '.add-column, .column-del', // …not the "add column" tile or the 🗑 button
                handle: '.column-head',         // grab the header to move the whole column
                preventOnFilter: false,         // let the 🗑 click through instead of eating it
                delay: delayMs,                 // press-and-hold a beat before it lifts
                delayOnTouchOnly: false,        // same feel with mouse and touch
                touchStartThreshold: 4,
                forceFallback: true,            // our own clone → consistent look + smooth touch
                animation: animationMs,         // neighbours slide to make room
                easing: 'cubic-bezier(0.22, 1, 0.36, 1)',
                ghostClass: 'col-ghost',        // the landing-slot placeholder
                chosenClass: 'col-chosen',      // the picked column
                dragClass: 'col-dragging',      // the floating clone under the cursor
                fallbackTolerance: 4,
                disabled: phone(),
                onEnd: function (evt) {
                    // Report the new order by column id, read straight from the DOM —
                    // robust regardless of where the non-draggable tiles sit.
                    var ids = Array.prototype.map.call(
                        el.querySelectorAll(':scope > .column'),
                        function (c) { return parseInt(c.getAttribute('data-col-id'), 10); }
                    ).filter(function (n) { return !isNaN(n); });
                    // Which column just landed — so Blazor can play its settle animation.
                    var dropped = parseInt(evt.item.getAttribute('data-col-id'), 10);
                    dotnetRef.invokeMethodAsync('OnColumnsReordered', ids, isNaN(dropped) ? -1 : dropped);
                }
            });

            if (!resizeHooked) {
                resizeHooked = true;
                window.addEventListener('resize', function () {
                    var e = document.getElementById(CONTAINER_ID);
                    if (e && e._kbSortable) e._kbSortable.option('disabled', phone());
                });
            }
        }
    };
})();
