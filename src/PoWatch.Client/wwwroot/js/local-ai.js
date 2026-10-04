// The browser's own language model (Chrome's Prompt API), used to reword a recap when the server has
// no model configured. Free, private and offline; absent in most browsers, and then this returns null.
(function () {
    'use strict';

    async function rewrite(system, user) {
        try {
            if (!('LanguageModel' in self) || (await LanguageModel.availability()) !== 'available') return null;
            const session = await LanguageModel.create({ initialPrompts: [{ role: 'system', content: system }] });
            try {
                return (await session.prompt(user)).trim();
            } finally {
                session.destroy();
            }
        } catch {
            return null;
        }
    }

    window.powatchLocalAi = { rewrite };
})();
