// Side-by-side match viewer (Views/Review/Compare).
//
// Each matched passage is a <mark data-fragment="n"> in both panes. Clicking one,
// or pressing Enter/Space on it, highlights that passage in both panes and
// scrolls the *other* pane to it; the passage buttons scroll both. Only the
// panes scroll, never the page, so the two documents stay side by side.

const panes = [...document.querySelectorAll('[data-match-pane]')];

if (panes.length === 2) {
    const [submitted, source] = panes;
    const otherPane = (pane) => (pane === submitted ? source : submitted);

    const marksFor = (root, fragment) => root.querySelectorAll(`[data-fragment="${CSS.escape(fragment)}"]`);

    const activate = (fragment) => {
        document.querySelectorAll('.match-fragment.is-active')
            .forEach((mark) => mark.classList.remove('is-active'));

        marksFor(document, fragment).forEach((mark) => mark.classList.add('is-active'));
    };

    const scrollPaneTo = (pane, fragment) => {
        const [first] = marksFor(pane, fragment);

        if (!first) {
            return;
        }

        // The pane is position: relative, so offsetTop is measured from its top.
        // A third of the way down leaves context visible above the passage.
        pane.scrollTo({ top: first.offsetTop - pane.clientHeight / 3, behavior: 'smooth' });
    };

    const show = (pane, fragment) => {
        activate(fragment);
        scrollPaneTo(otherPane(pane), fragment);
    };

    for (const pane of panes) {
        pane.addEventListener('click', (event) => {
            const mark = event.target.closest('[data-fragment]');

            if (mark) {
                show(pane, mark.dataset.fragment);
            }
        });

        pane.addEventListener('keydown', (event) => {
            const mark = event.target.closest('[data-fragment]');

            if (mark && (event.key === 'Enter' || event.key === ' ')) {
                event.preventDefault();
                show(pane, mark.dataset.fragment);
            }
        });
    }

    for (const button of document.querySelectorAll('[data-fragment-jump]')) {
        button.addEventListener('click', () => {
            const fragment = button.dataset.fragmentJump;

            activate(fragment);
            scrollPaneTo(submitted, fragment);
            scrollPaneTo(source, fragment);
        });
    }
}
