/**
 * Migration Constraint Safety and Expand-Contract Validator
 *
 * Enforces zero-downtime rules for database migrations:
 * - Prohibits dropping PRIMARY KEY, CHECK constraints, and non-allowlisted UNIQUE constraints.
 * - Allows dropping foreign keys ONLY when an approved replacement constraint is added on the same table.
 * - Prevents false-positives by strictly parsing real method invocations in the Up() method only,
 *   rejecting comments, string literals, Down() methods, and cross-table additions.
 */

/**
 * Narrow allowlist of approved constraint replacement operations.
 * Each entry requires verified replacement constraint on the expected table in the same migration.
 */
export const ALLOWED_CONSTRAINT_REPLACEMENTS = [
  { droppedConstraint: 'fk_branch_item_availabilities_item_variants_variant_id', replacementConstraint: 'fk_branch_item_availabilities_variants_tenant_branch_variant', table: 'branch_item_availabilities', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_branch_item_availabilities_menu_items_item_id', replacementConstraint: 'fk_branch_item_availabilities_menu_items_tenant_branch_item', table: 'branch_item_availabilities', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_item_variants_menu_items_menu_item_id', replacementConstraint: 'fk_item_variants_menu_items_tenant_branch_item', table: 'item_variants', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_item_variants_menus_tenant_id_menu_id', replacementConstraint: 'fk_item_variants_menus_tenant_branch_menu', table: 'item_variants', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_menu_categories_menus_tenant_id_menu_id', replacementConstraint: 'fk_menu_categories_menus_tenant_branch_menu', table: 'menu_categories', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_item_modifier_assignments_menu_items_item_id', replacementConstraint: 'fk_item_modifier_assignments_menu_items_tenant_branch_item', table: 'menu_item_modifier_group_assignments', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_item_modifier_assignments_modifier_groups_group_id', replacementConstraint: 'fk_item_modifier_assignments_groups_tenant_branch_group', table: 'menu_item_modifier_group_assignments', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_menu_items_categories_tenant_id_menu_id_category_id', replacementConstraint: 'fk_menu_items_categories_tenant_branch_menu_cat', table: 'menu_items', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_menu_items_menus_tenant_id_menu_id', replacementConstraint: 'fk_menu_items_menus_tenant_branch_menu', table: 'menu_items', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_menu_items_preparation_stations_station_id', replacementConstraint: 'fk_menu_items_prep_stations_tenant_branch_station', table: 'menu_items', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'fk_modifier_options_modifier_groups_group_id', replacementConstraint: 'fk_modifier_options_modifier_groups_tenant_branch_group', table: 'modifier_options', reason: 'Composite tenant/branch FK (Phase 5.5)' },
  { droppedConstraint: 'AK_menus_tenant_id_id', replacementConstraint: 'AK_menus_tenant_id_branch_id_id', table: 'menus', type: 'unique', allowedFile: '20261004211028_AddCatalogCrossBranchReferentialConstraintsAndOutbox', reason: 'Branch-scoped menu AK (Phase 5.5)' },
  { droppedConstraint: 'fk_menu_items_prep_stations_tenant_branch_station', replacementConstraint: 'fk_menu_items_prep_stations_tenant_branch_station', table: 'menu_items', reason: 'Restrict delete behavior hardening (Phase 5 closing)' },
];

export const ALLOWED_FK_REPLACEMENTS = ALLOWED_CONSTRAINT_REPLACEMENTS;

/**
 * Strips comments from C# or SQL source code.
 */
export function stripComments(code, isCSharp = true) {
  if (typeof code !== 'string') return '';
  if (isCSharp) {
    return code
      .replace(/\/\*[\s\S]*?\*\//g, ' ')
      .replace(/\/\/.*$/gm, ' ');
  }
  return code
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/--.*$/gm, ' ');
}

/**
 * Extracts ONLY the body of the Up() method in C# migrations.
 * Down() method and outer declarations are strictly excluded.
 */
export function extractUpMethodContent(content) {
  if (typeof content !== 'string') return '';

  const upRegex = /protected\s+override\s+void\s+Up\s*\(\s*MigrationBuilder\s+\w+\s*\)/;
  const upMatch = upRegex.exec(content);
  if (!upMatch) {
    const downIndex = content.search(/protected\s+override\s+void\s+Down\s*\(/);
    if (downIndex !== -1) {
      return content.slice(0, downIndex);
    }
    return content;
  }

  const braceStart = content.indexOf('{', upMatch.index);
  if (braceStart === -1) return content.slice(upMatch.index);

  let depth = 0;
  let inString = false;
  let stringChar = null;
  let isEscaped = false;

  for (let i = braceStart; i < content.length; i++) {
    const char = content[i];

    if (inString) {
      if (isEscaped) {
        isEscaped = false;
      } else if (char === '\\') {
        isEscaped = true;
      } else if (char === stringChar) {
        inString = false;
      }
      continue;
    }

    if (char === '"' || char === "'") {
      inString = true;
      stringChar = char;
      continue;
    }

    if (char === '{') {
      depth++;
    } else if (char === '}') {
      depth--;
      if (depth === 0) {
        return content.slice(braceStart + 1, i);
      }
    }
  }

  return content.slice(braceStart + 1);
}

/**
 * Parses C# MigrationBuilder method invocations, ignoring string literals.
 */
function parseCsMethodInvocations(code, methodName) {
  const invocations = [];
  let inString = false;
  let stringChar = null;
  let isEscaped = false;
  let isVerbatim = false;

  const targetPrefix = `migrationBuilder.${methodName}`;

  for (let i = 0; i < code.length; i++) {
    const char = code[i];

    if (inString) {
      if (!isVerbatim && isEscaped) {
        isEscaped = false;
      } else if (!isVerbatim && char === '\\') {
        isEscaped = true;
      } else if (isVerbatim && char === '"' && code[i + 1] === '"') {
        i++; // Escaped quote in verbatim string
      } else if (char === stringChar) {
        inString = false;
        isVerbatim = false;
      }
      continue;
    }

    if (char === '@' && code[i + 1] === '"') {
      inString = true;
      stringChar = '"';
      isVerbatim = true;
      i++;
      continue;
    }

    if (char === '"' || char === "'") {
      inString = true;
      stringChar = char;
      isVerbatim = false;
      continue;
    }

    // Check if matching target prefix (case-insensitive for robustness)
    if (code.slice(i, i + targetPrefix.length).toLowerCase() === targetPrefix.toLowerCase()) {
      const openParenIndex = code.indexOf('(', i + targetPrefix.length);
      if (openParenIndex !== -1 && /^\s*$/.test(code.slice(i + targetPrefix.length, openParenIndex))) {
        // Find matching closing paren
        let parenDepth = 1;
        let callInString = false;
        let callStringChar = null;
        let callEscaped = false;
        let closeParenIndex = -1;

        for (let j = openParenIndex + 1; j < code.length; j++) {
          const c = code[j];
          if (callInString) {
            if (callEscaped) {
              callEscaped = false;
            } else if (c === '\\') {
              callEscaped = true;
            } else if (c === callStringChar) {
              callInString = false;
            }
            continue;
          }

          if (c === '"' || c === "'") {
            callInString = true;
            callStringChar = c;
            continue;
          }

          if (c === '(') parenDepth++;
          else if (c === ')') {
            parenDepth--;
            if (parenDepth === 0) {
              closeParenIndex = j;
              break;
            }
          }
        }

        const argsText = closeParenIndex !== -1
          ? code.slice(openParenIndex + 1, closeParenIndex)
          : code.slice(openParenIndex + 1);

        const nameMatch = argsText.match(/\bname:\s*["']([^"']+)["']/i);
        const tableMatch = argsText.match(/\btable:\s*["']([^"']+)["']/i);
        const schemaMatch = argsText.match(/\bschema:\s*["']([^"']+)["']/i);

        invocations.push({
          methodName,
          startIndex: i,
          name: nameMatch ? nameMatch[1] : null,
          table: tableMatch ? tableMatch[1] : null,
          schema: schemaMatch ? schemaMatch[1] : null,
          argsText,
        });

        if (closeParenIndex !== -1) {
          i = closeParenIndex;
        }
      }
    }
  }

  return invocations;
}

/**
 * Parses SQL ALTER TABLE ... DROP CONSTRAINT statements.
 */
function parseSqlDropConstraints(sqlContent) {
  const drops = [];
  const regex = /ALTER\s+TABLE\s+(?:ONLY\s+)?(?:([a-zA-Z0-9_]+)\.)?([a-zA-Z0-9_]+)\s+DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?([a-zA-Z0-9_]+)["']?/gi;
  let match;
  while ((match = regex.exec(sqlContent)) !== null) {
    drops.push({
      schema: match[1] || null,
      table: match[2],
      name: match[3],
      startIndex: match.index,
    });
  }
  return drops;
}

/**
 * Parses SQL ALTER TABLE ... ADD CONSTRAINT statements.
 */
function parseSqlAddConstraints(sqlContent) {
  const adds = [];
  const regex = /ALTER\s+TABLE\s+(?:ONLY\s+)?(?:([a-zA-Z0-9_]+)\.)?([a-zA-Z0-9_]+)\s+ADD\s+CONSTRAINT\s+["']?([a-zA-Z0-9_]+)["']?/gi;
  let match;
  while ((match = regex.exec(sqlContent)) !== null) {
    adds.push({
      schema: match[1] || null,
      table: match[2],
      name: match[3],
      startIndex: match.index,
    });
  }
  return adds;
}

/**
 * Validates constraint safety for both C# migrations and raw SQL files.
 */
export function validateConstraintSafety(rawContent, options = {}) {
  const violations = [];
  if (!rawContent || typeof rawContent !== 'string') return violations;

  const isCSharp = options.isCSharp === true;
  const fileName = options.fileName || options.filePath || '';

  // 1. Comments must never be evaluated as code
  const codeWithoutComments = stripComments(rawContent, isCSharp);

  // 2. Only Up method should be checked for C# migrations
  const content = isCSharp ? extractUpMethodContent(codeWithoutComments) : codeWithoutComments;

  // 3. Strict blocking of Primary Key drops
  const pkDropRegex = /\b(?:DROP\s+PRIMARY\s+KEY|DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?(?:pk_[a-zA-Z0-9_]*|[a-zA-Z0-9_]*_pkey)["']?)\b/i;
  if (pkDropRegex.test(content) || /migrationBuilder\s*\.\s*DropPrimaryKey\b/i.test(content)) {
    violations.push('DROP PRIMARY KEY/CONSTRAINT is strictly prohibited before cutover');
  }

  // 4. Strict blocking of Check constraint drops
  const ckDropRegex = /\b(?:DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?(?:ck_[a-zA-Z0-9_]*|[a-zA-Z0-9_]*_check)["']?)\b/i;
  if (ckDropRegex.test(content) || /migrationBuilder\s*\.\s*DropCheckConstraint\b/i.test(content)) {
    violations.push('DROP CHECK CONSTRAINT is strictly prohibited before cutover');
  }

  // 5. Unique constraint drop check
  const uqDropRegex = /\b(?:DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?(?:uq_[a-zA-Z0-9_]*|[a-zA-Z0-9_]*_key)["']?)\b/i;
  if (uqDropRegex.test(content)) {
    const uqMatch = content.match(/DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?([^"'\s;]+)["']?/i);
    const uqName = uqMatch ? uqMatch[1] : '';
    const isAllowlisted = ALLOWED_CONSTRAINT_REPLACEMENTS.some(
      r => r.type === 'unique' && r.droppedConstraint.toLowerCase() === uqName.toLowerCase()
    );
    if (!isAllowlisted) {
      violations.push('DROP UNIQUE CONSTRAINT is strictly prohibited before cutover');
    }
  }

  if (/migrationBuilder\s*\.\s*DropUniqueConstraint\b/i.test(content)) {
    const csUqDrops = parseCsMethodInvocations(content, 'DropUniqueConstraint');
    for (const call of csUqDrops) {
      const isAllowlisted = ALLOWED_CONSTRAINT_REPLACEMENTS.some(
        r => r.type === 'unique' && r.droppedConstraint.toLowerCase() === (call.name || '').toLowerCase()
      );
      if (!isAllowlisted) {
        violations.push('DROP UNIQUE CONSTRAINT is strictly prohibited before cutover');
      }
    }
  }

  // Parse drops and additions
  let droppedConstraints = [];
  let addedFks = [];
  let addedUqs = [];

  if (isCSharp) {
    const fkDrops = parseCsMethodInvocations(content, 'DropForeignKey');
    const uqDrops = parseCsMethodInvocations(content, 'DropUniqueConstraint');
    droppedConstraints = [
      ...fkDrops.map(d => ({ ...d, type: 'fk' })),
      ...uqDrops.map(d => ({ ...d, type: 'unique' })),
    ];
    addedFks = parseCsMethodInvocations(content, 'AddForeignKey');
    addedUqs = parseCsMethodInvocations(content, 'AddUniqueConstraint');
  } else {
    droppedConstraints = parseSqlDropConstraints(content).map(d => ({ ...d, type: 'unknown' }));
    const sqlAdds = parseSqlAddConstraints(content);
    addedFks = sqlAdds;
    addedUqs = sqlAdds;
  }

  for (const dropped of droppedConstraints) {
    const dropName = dropped.name || '';
    const dropTable = dropped.table || '';
    const lowerName = dropName.toLowerCase();
    const lowerTable = dropTable.toLowerCase();

    // Skip PK and CK already flagged
    if (lowerName.startsWith('pk_') || lowerName.endsWith('_pkey') ||
        lowerName.startsWith('ck_') || lowerName.endsWith('_check')) {
      continue;
    }

    const allowEntry = ALLOWED_CONSTRAINT_REPLACEMENTS.find(
      r => r.droppedConstraint.toLowerCase() === lowerName
    );

    if (!allowEntry) {
      violations.push(`Dropping constraint '${dropName}' is prohibited before cutover (unauthorized constraint drop)`);
      continue;
    }

    const expectedReplacement = allowEntry.replacementConstraint.toLowerCase();
    const expectedTable = allowEntry.table.toLowerCase();

    if (lowerTable && expectedTable && lowerTable !== expectedTable) {
      violations.push(`Dropping constraint '${dropName}' on table '${dropTable}' does not match expected table '${allowEntry.table}'`);
      continue;
    }

    // Historical exception restriction check for AK_menus_tenant_id_id
    if (allowEntry.allowedFile && fileName) {
      if (!fileName.includes(allowEntry.allowedFile)) {
        violations.push(`Dropping unique constraint '${dropName}' is not allowed in '${fileName}'. Only permitted in '${allowEntry.allowedFile}'.`);
        continue;
      }
    }

    if (allowEntry.type === 'unique') {
      const hasReplacement = addedUqs.some(add =>
        (add.name || '').toLowerCase() === expectedReplacement &&
        (!add.table || add.table.toLowerCase() === expectedTable) &&
        add.startIndex > dropped.startIndex
      );

      if (!hasReplacement) {
        violations.push(`Dropping constraint '${dropName}' requires replacement '${allowEntry.replacementConstraint}' in the same migration, but replacement was not found.`);
      }
      continue;
    }

    // Foreign Key Replacement: must find an AddForeignKey / ADD CONSTRAINT on expected table AFTER the drop
    const matchingAdds = addedFks.filter(add =>
      (add.name || '').toLowerCase() === expectedReplacement &&
      (!add.table || add.table.toLowerCase() === expectedTable) &&
      add.startIndex > dropped.startIndex
    );

    if (matchingAdds.length === 0) {
      violations.push(`Dropping constraint '${dropName}' requires replacement '${allowEntry.replacementConstraint}' in the same migration, but replacement was not found.`);
    }
  }

  return violations;
}
