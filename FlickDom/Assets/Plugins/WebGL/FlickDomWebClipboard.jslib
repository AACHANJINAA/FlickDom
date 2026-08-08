mergeInto(LibraryManager.library, {
  FlickDomCopyTextToClipboard: function (textPtr) {
    var text = UTF8ToString(textPtr);
    if (!text) {
      return;
    }

    var fallbackCopy = function (value) {
      var textarea = document.createElement("textarea");
      textarea.value = value;
      textarea.setAttribute("readonly", "");
      textarea.style.position = "fixed";
      textarea.style.left = "-9999px";
      textarea.style.top = "0";
      textarea.style.opacity = "0";
      textarea.style.pointerEvents = "none";
      document.body.appendChild(textarea);
      textarea.focus();
      textarea.select();

      try {
        document.execCommand("copy");
      } catch (error) {
        console.warn("[FlickDom] Clipboard fallback failed:", error);
      }

      document.body.removeChild(textarea);
    };

    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).catch(function (error) {
        console.warn("[FlickDom] Async clipboard failed, using fallback:", error);
        fallbackCopy(text);
      });
      return;
    }

    fallbackCopy(text);
  }
});
