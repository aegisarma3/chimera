using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Catflap
{
    public partial class Manifest
    {
        public const int VERSION = 5;

        public static JsonSchema Schema = JsonSchema.Parse(@"{
	'$schema': 'http://json-schema.org/draft-04/schema#',
	'description': 'Catflap Manifest',
	'type': 'object',
	'properties': {
        'locked': { 'type': 'string', 'required': false },
        'version':  { 'type': 'integer', 'required': true, 'minimum': 1 },
        'title': { 'type': 'string', 'required': false },
        'baseUrl': { 'type': 'string', 'required': true },
        'rsyncUrl': { 'type': 'string', 'required': true },
        'revision': { 'type': 'string', 'required': false },
        'textColor': { 'type': 'string', 'required': false },
        'darkTheme': { 'type': 'boolean', 'required': false },
        'dropShadows': { 'type': 'boolean', 'required': false },

        'warnWhenSetupWithUntracked': { 'type': 'boolean', 'required': false },

        'fuzzy': { 'type': 'boolean', 'required': false },
        'ignoreCase': { 'type': 'boolean', 'required': false },
        'ignoreExisting': { 'type': 'boolean', 'required': false },
        'purge': { 'type': 'boolean', 'required': false },

        'runAction': { 'type': 'object', 'required': false },

        'sync': { 'type': 'array', 'required': true, 'items':  {
                'type': 'object',
                'properties': {
                    'name': { 'type': 'string', 'required': true },
                    'revision': { 'type': 'integer', 'required': false },
                    'type': { 'type': 'string', 'required': false },
                    'mode': { 'type': 'string', 'required': false },
                    'size': { 'type': 'integer', 'required': false, 'minimum': 0 },
                    'count': { 'type': 'integer', 'required': false, 'minimum': 0 },
                    'purge': { 'type': 'boolean', 'required': false },
                    'mtime': { 'type': 'string', 'required': false },
                    'fuzzy': { 'type': 'boolean', 'required': false },
                    'ignoreCase': { 'type': 'boolean', 'required': false },
                    'ignoreExisting': { 'type': 'boolean', 'required': false }
                }
            }
        },
    },
}");

    }
}
