// A missing uploaded product image must not become a broken photograph or be
// mistaken for the actual item. Official brand images are never substituted.
(() => {
    "use strict";
    function useProductFallback(img) {
        if (!(img instanceof HTMLImageElement) || !img.hasAttribute("data-product-image") || img.dataset.fallbackApplied) return;
        const fallback = img.dataset.fallbackSrc;
        if (!fallback || new URL(fallback, document.baseURI).origin !== location.origin) return;
        img.dataset.fallbackApplied = "true";
        img.src = fallback;
        img.alt = "Ảnh minh họa nông sản — ảnh sản phẩm chưa khả dụng";
        const container = img.closest(".store-product-picture, .store-detail-picture, figure");
        if (!container) return;
        container.classList.add("is-placeholder");
        let caption = container.querySelector(".store-photo-caption, figcaption");
        if (!caption) {
            caption = document.createElement(container.tagName === "FIGURE" ? "figcaption" : "span");
            caption.className = "store-photo-caption";
            container.append(caption);
        }
        caption.textContent = "Ảnh minh họa";
        caption.hidden = false;
    }
    document.addEventListener("error", event => useProductFallback(event.target), true);
    document.querySelectorAll("img[data-product-image]").forEach(img => {
        if (img.complete && img.naturalWidth === 0) useProductFallback(img);
    });
})();
