import { describe, expect, it } from 'vitest';
import { parseKeyValueText, stringifyKeyValue } from './hooks';

describe('MCP storage helpers', () => {
  it('round-trips environment dictionaries', () => {
    const source = {
      FOO: 'bar',
      EMPTY: '',
    };

    expect(parseKeyValueText(stringifyKeyValue(source))).toEqual(source);
  });

  it('ignores blank lines and trims keys', () => {
    expect(parseKeyValueText('\n FOO = bar \n INVALID \n')).toEqual({
      FOO: 'bar',
      INVALID: '',
    });
  });
});
