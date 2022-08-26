package genshin_piano;

import java.io.Serial;
import java.util.Collections;
import java.util.Enumeration;
import java.util.LinkedHashSet;
import java.util.Properties;
import java.util.Set;

/**
 * genshin_piano.OrderedProperties
 * @author hanwl
 * @date 2018-11-13
 * @userd 使得Properties有序
 */
public class OrderedProperties extends Properties {

    @Serial
    private static final long serialVersionUID = 4710927773256743817L;

    private final LinkedHashSet<Object> keys = new LinkedHashSet<>();

    @Override
    public Enumeration<Object> keys() {
        return Collections.enumeration(keys);
    }

    @Override
    public Object put(Object key, Object value) {
        keys.add(key);
        return super.put(key, value);
    }

    @Override
    public Set<Object> keySet() {
        return keys;
    }

    @Override
    public Set<String> stringPropertyNames() {
        Set<String> set = new LinkedHashSet<>();

        for (Object key : this.keys) {
            set.add((String) key);
        }

        return set;
    }
}