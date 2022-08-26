package genshin_piano;

import java.io.File;
import java.net.URISyntaxException;

public class getJarPath {
    public String getJarPath() {
//        String jarFolder = null;
//        try {
//            jarFolder = new File(readMe.class.getProtectionDomain().getCodeSource().getLocation().toURI().getPath()).getParentFile().getPath().replace('\\', '/');
//        } catch (URISyntaxException e) {
//            e.printStackTrace();
//            System.out.println("jar获取路径错误");
//        }
//        return  jarFolder;
//    }

        String path = null;
        try {
            path = this.getClass().getProtectionDomain().getCodeSource().getLocation().toURI().getPath();
        }
        catch (URISyntaxException e) {
            e.printStackTrace();
        }
        if (System.getProperty("os.name").contains("dows")) {
            assert path != null;
            path = path.substring(1);
        }
        assert path != null;
        if (path.contains("jar")) {
            path = path.substring(0, path.lastIndexOf("."));
            return path.substring(0, path.lastIndexOf("/"));
        }
        return path.replace("target/classes/", "");
    }


}
