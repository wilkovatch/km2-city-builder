import sys, os, glob, gzip, json, copy, shutil

class MigrationHelper:
    def __init__(self, path):
        self.path = path
        self.pref_data = self.read_local('preferences.json', False)

    def read(self, full_name, gzipped):
        orig = full_name
        if gzipped:
            f = gzip.open(orig, 'rb')
        else:
            f = open(orig)  
        data = json.load(f)
        f.close()
        return data

    def read_local(self, name, gzipped):
        orig = self.path + name
        return self.read(orig, gzipped)

    def get_value(self, data, key, default):
        if key not in data.keys(): return default
        else: return data[key]
        
    def get_pref(self, key, default):
        return self.get_value(self.pref_data, key, default)

    def read_and_backup(self, name, gzipped):
        prev_v_dat = self.get_pref('coreVersion', 0)
        orig = self.path + name
        backup = self.path + name + '.v' + str(prev_v_dat) + '.bak'
        data = self.read_local(name, gzipped)
        shutil.copyfile(orig, backup)
        return data

    def save(self, data_out, name, gzipped):
        orig = self.path + name
        if gzipped:
            with gzip.open(orig, "w") as f_out:
                f_out.write(json.dumps(data_out, indent=4).encode('utf-8'))
        else:
            with open(orig, "w") as f_out:
                json.dump(data_out, f_out, indent=4)

    def get_migrations_todo(self, cur_v_prog, cur_v_feat):
        core = self.get_pref('core', 'MidtownMadness2') #the city builder started with MM2 support only
        core_path = os.path.normpath(os.path.dirname(os.path.realpath(__file__)) + '/../cores/' + core) + '/'
        settings = self.read(core_path + 'settings.json', False)
        v_core = self.get_value(settings, 'coreVersion', 0)
        v_crft = self.get_value(settings, 'coreFeatureVersion', 0)
        v_prog = self.get_value(settings, 'programVersion', 0)
        v_feat = self.get_value(settings, 'featureVersion', 0)
        city_v_core = self.get_pref('coreVersion', 0)
        city_v_crft = self.get_pref('coreFeatureVersion', 0)
        if v_prog > cur_v_prog or v_feat > cur_v_feat: raise Exception("MIGRATION_ERROR_CORE_IS_FOR_NEWER_PROGRAM")
        elif v_prog < cur_v_prog: raise Exception("MIGRATION_ERROR_CORE_IS_FOR_OLDER_PROGRAM")
        elif v_core == city_v_core and v_crft >= city_v_crft: return []
        elif v_core < city_v_core or v_crft < city_v_crft: raise Exception("MIGRATION_ERROR_CITY_IS_FOR_NEWER_CORE")
        migrations_path = core_path + '/migrations/*.py'
        files = glob.glob(migrations_path)
        res = []
        has_last_migration = False
        expected_migrations = v_core - city_v_core
        for f in files:
            fn = os.path.basename(f)
            v_parts = fn.split('.')
            if len(v_parts) > 2: raise Exception("MIGRATION_ERROR_WRONG_MIGRATION_NAME")
            v = v_parts[0]
            if not v.isnumeric(): raise Exception("MIGRATION_ERROR_WRONG_MIGRATION_NAME")
            vi = int(v)
            if vi > city_v_core and vi <= v_core:
                res.append(f)
                if vi == v_core: has_last_migration = True
        if len(res) < expected_migrations or not has_last_migration: raise Exception("MIGRATION_ERROR_MISSING_MIGRATIONS")
        return res
