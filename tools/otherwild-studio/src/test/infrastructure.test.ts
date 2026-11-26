import { describe, expect, it } from 'vitest'

describe('Otherwild Studio test infrastructure', () => {
    it('executes TypeScript unit tests', () => {
        const workspace = {
            id: 'project',
            title: 'Project',
        }

        expect(workspace.id).toBe('project')
        expect(workspace.title).toBe('Project')
    })
})