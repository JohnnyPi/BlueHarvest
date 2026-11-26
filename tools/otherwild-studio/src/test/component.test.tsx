// @vitest-environment jsdom

import { useState } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

function TestComponent() {
    const [opened, setOpened] = useState(false)

    return (
        <section>
            <h1>Otherwild Studio</h1>

            <button onClick={() => setOpened(true)}>
                Open project
            </button>

            {opened && <p>Project opened</p>}
        </section>
    )
}

describe('React component test infrastructure', () => {
    it('renders an accessible React component', () => {
        render(<TestComponent />)

        expect(
            screen.getByRole('heading', {
                name: 'Otherwild Studio',
            }),
        ).toBeInTheDocument()
    })

    it('supports user interaction and state updates', async () => {
        const user = userEvent.setup()

        render(<TestComponent />)

        expect(
            screen.queryByText('Project opened'),
        ).not.toBeInTheDocument()

        await user.click(
            screen.getByRole('button', {
                name: 'Open project',
            }),
        )

        expect(
            screen.getByText('Project opened'),
        ).toBeInTheDocument()
    })
})